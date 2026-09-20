using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Exceptions;

/// <summary>
/// Pins EXC-06…EXC-15: each documented cause reaches the caller as the one §8 type that names it,
/// and the two causes the contract deliberately keeps outside the hierarchy stay outside it.
/// </summary>
/// <remarks>
/// The assertions here are exact-type on purpose. <see cref="BinaryLimitException"/> derives from
/// <see cref="BinaryFormatException"/>, so a cause that must be reported as malformed is only pinned
/// by a test that a limit breach would fail.
/// </remarks>
public class ExceptionMappingTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- EXC-06: malformed structure → BinaryFormatException --------------------------------------

    [Fact]
    public void Deserialize_NegativeCollectionCount_ThrowsFormatNotLimit()
    {
        // A count below zero is not a resource question: no configuration could make it legal.
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(-1);
        }));

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<List<int>>(frame));
    }

    [Fact]
    public void Deserialize_PayloadShorterThanTheValueItFrames_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(new byte[2]);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<long>(frame));
    }

    [Fact]
    public void Deserialize_AStreamWithoutTheMagicNumber_ThrowsFormat()
    {
        // Without AllowV0Fallback the router refuses to guess rather than reading the bytes as V0.
        using var source = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(source));
    }

    // --- EXC-07: parseable but over a configured ceiling → BinaryLimitException --------------------

    [Fact]
    public void Deserialize_CollectionCountAboveTheConfiguredLimit_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxCollectionLength = 2 });
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write(3);
        }));

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<List<int>>(frame));
    }

    [Fact]
    public void Deserialize_DepthBeyondTheConfiguredMaximum_ThrowsLimitNotStackOverflow()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxDepth = 4 });

        Assert.Throws<BinaryLimitException>(
            () => serializer.Deserialize<Tree>(Wire.NestedCollections(64)));
    }

    // --- EXC-08: recognized but unsupported → BinaryFormatNotSupportedException --------------------

    [Fact]
    public void Deserialize_AKnownMagicWithAnUnsupportedVersion_ThrowsNotSupported()
    {
        byte[] frame = Wire.FrameWith(Wire.Payload(writer => writer.Write(123)), version: 99);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnUndefinedAlgorithmIdentifier_ThrowsNotSupported()
    {
        byte[] frame = Wire.FrameWith(Wire.Payload(writer => writer.Write(123)), compression: 200);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_ACustomAlgorithmThisConfigurationNeverRegistered_ThrowsNotSupported()
    {
        var writer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Sum8())
                .RegisterCustomChecksum(Sum8.RegisteredName, () => new Sum8())
                .Build());

        byte[] payload = writer.Serialize(123);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<int>(payload));
    }

    // --- EXC-09: checksum or tag failure → BinaryIntegrityException --------------------------------

    [Fact]
    public void Deserialize_AChecksumThatDoesNotMatchThePayload_ThrowsIntegrity()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build());

        byte[] body = Wire.Payload(writer => writer.Write(123));
        byte[] frame = Wire.FrameWith(
            body,
            checksumAlgorithm: (byte)ChecksumAlgorithm.Crc32,
            checksum: [0, 0, 0, 0]);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_AnEncryptedPayloadWithTheWrongKey_ThrowsIntegrity()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);

        Assert.Throws<BinaryIntegrityException>(() => Encrypted(NewKey()).Deserialize<int>(payload));
    }

    // --- EXC-10: key missing, unresolvable or mismatched → BinaryEncryptionKeyException ------------

    [Fact]
    public void Deserialize_AnEncryptedPayloadWithNoKeyConfigured_ThrowsKeyException()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);

        Assert.Throws<BinaryEncryptionKeyException>(
            () => new BinarySerializer().Deserialize<int>(payload));
    }

    [Fact]
    public void Deserialize_AResolverThatSuppliesNoKey_ThrowsKeyException()
    {
        byte[] payload = Encrypted(NewKey(), keyId: "primary").Serialize(123);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), _ => null, "primary")
                .Build());

        Assert.Throws<BinaryEncryptionKeyException>(() => serializer.Deserialize<int>(payload));
    }

    [Fact]
    public void Deserialize_OptionsBuiltFromAnEncryptedHeaderWithoutKeys_ThrowsKeyException()
    {
        // The reachable form of "an algorithm is configured but no key material was supplied": the
        // header names the algorithm, and the caller supplied nothing to decrypt with.
        byte[] payload = Encrypted(NewKey()).Serialize(123);
        using var source = new MemoryStream(payload);

        var options = BinarySerializerOptions.FromStream(source, key: null);

        Assert.Throws<BinaryEncryptionKeyException>(
            () => new BinarySerializer(options).Deserialize<int>(source));
    }

    [Fact]
    public void Resolve_AKeyIdThatTheProviderDoesNotHold_ThrowsKeyException()
    {
        using var provider = new StaticKeyProvider(NewKey(), "primary");

        Assert.Throws<BinaryEncryptionKeyException>(() => provider.Resolve("rotated"));
    }

    // --- EXC-11: caller-stream I/O failure → BinaryStreamException ---------------------------------

    [Fact]
    public void Serialize_IntoAFailingStream_ThrowsStream()
    {
        using var destination = new FailingStream(bytesBeforeFailure: 4);

        Assert.Throws<BinaryStreamException>(
            () => new BinarySerializer().Serialize(destination, new Person { Name = "Alice", Age = 1 }));
    }

    [Fact]
    public void Deserialize_FromAFailingStream_ThrowsStream()
    {
        using var source = new FailingStream(bytesBeforeFailure: 2);

        Assert.Throws<BinaryStreamException>(() => new BinarySerializer().Deserialize<int>(source));
    }

    // --- EXC-12: invalid CLR type, contract or graph semantics → BinaryTypeException ---------------

    [Fact]
    public void Serialize_ACycleWithoutPreserveReferences_ThrowsType()
    {
        var tree = new Tree();
        tree.Add(tree);

        Assert.Throws<BinaryTypeException>(() => new BinarySerializer().Serialize(tree));
    }

    [Fact]
    public void Serialize_ARuntimeTypeWithNoUnionMap_ThrowsType()
    {
        Base value = new Derived { Z = 1, A = 2 };

        Assert.Throws<BinaryTypeException>(() => new BinarySerializer().Serialize(value));
    }

    [Fact]
    public void Deserialize_IntoAnExistingInstanceOfAFormatterOwnedType_ThrowsType()
    {
        byte[] payload = new BinarySerializer().Serialize(new List<int> { 1, 2, 3 });

        Assert.Throws<BinaryTypeException>(
            () => new BinarySerializer().Deserialize(payload, new List<int>()));
    }

    // --- EXC-13: invalid configuration → BinaryConfigurationException ------------------------------

    [Fact]
    public void Build_AnInvalidLimit_ThrowsConfigurationNotFormat()
    {
        // A limit value is a property of the configuration, never of the data: it is the one place
        // a zero is a configuration error rather than a legal empty value.
        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxDepth = 0 })
                .Build());
    }

    [Fact]
    public void Build_AWriteVersionNoPipelineSupports_ThrowsConfigurationNotNotSupported()
    {
        // Unlike a payload declaring an unknown version, this is the caller's own mistake and is
        // caught where the configuration is assembled, not at the first Serialize.
        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure().WithVersion(7).Build());
    }

    // --- EXC-14: a null public argument → ArgumentNullException, never a Viper type ----------------

    [Fact]
    public void Serialize_ANullDestinationStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Serialize(null!, 123));
    }

    [Fact]
    public void Deserialize_ANullSourceStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Deserialize<int>((Stream)null!));
    }

    [Fact]
    public void Deserialize_ANullByteArray_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Deserialize<int>((byte[])null!));
    }

    [Fact]
    public void Deserialize_ANullExistingInstance_ThrowsArgumentNull()
    {
        byte[] payload = new BinarySerializer().Serialize(new Person { Name = "Alice", Age = 1 });

        Assert.Throws<ArgumentNullException>(
            () => new BinarySerializer().Deserialize(payload, (Person)null!));
    }

    [Fact]
    public void WithCompressionAndFriends_ANullAlgorithm_ThrowArgumentNull()
    {
        var builder = BinarySerializerOptions.Configure();

        Assert.Throws<ArgumentNullException>(() => builder.WithCompression(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithChecksum(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithEncryption(null!, NewKey()));
        Assert.Throws<ArgumentNullException>(
            () => builder.WithEncryption(new Aes256Gcm(), (IKeyProvider)null!));
    }

    [Fact]
    public void StreamExtensions_ANullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.Serialize((Stream)null!, 123));
        Assert.Throws<ArgumentNullException>(() => StreamExtensions.Deserialize<int>((Stream)null!));
    }

    [Fact]
    public void Peek_ANullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => BinaryFormatInspector.Peek(null!));
    }

    // --- EXC-15: a required capability such as seekability → NotSupportedException -----------------

    [Fact]
    public void Deserialize_FromANonSeekableStream_ThrowsNotSupported()
    {
        // Reading must detect the format version before consuming anything, which needs a rewind.
        byte[] payload = new BinarySerializer().Serialize(123);
        using var source = new NonSeekableStream(payload);

        Assert.Throws<NotSupportedException>(() => new BinarySerializer().Deserialize<int>(source));
    }

    [Fact]
    public void Serialize_AKeyedContractToANonSeekableV0Destination_ThrowsNotSupported()
    {
        // A keyed field's length is patched after the field is written; V0 writes straight through.
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).Build());

        using var destination = new NonSeekableWriteStream();

        Assert.Throws<NotSupportedException>(
            () => serializer.Serialize(destination, new NewSchema { Kept = new Node { Value = 1 } }));
    }

    private static BinarySerializer Encrypted(byte[] key, string? keyId = null) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), key, keyId)
            .Build());
}
