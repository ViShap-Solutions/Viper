using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins XEP-01 to XEP-07: for one logical value under one configuration every entry point of
/// sections 3.1 and 3.2 must agree — the same value back, and a payload one entry point wrote read
/// by any other. An entry point that quietly framed a value differently would be a second wire
/// format hiding behind a convenience overload.
/// </summary>
/// <remarks>
/// The suite names no profile. It is run under the default V1 frame, under the full V1 envelope, and
/// under the headerless format, by the three subclasses at the end of this file.
/// </remarks>
public abstract class CrossEntryPoint
{
    /// <summary>The configuration of the profile under test.</summary>
    protected abstract BinarySerializerOptions Options { get; }

    /// <summary>
    /// Whether the payload carries a header, which is what the key-, resolver- and limits-only
    /// stream extensions configure themselves from.
    /// </summary>
    protected virtual bool SelfDescribing => true;

    /// <summary>
    /// Whether two writes of one value produce identical bytes. Authenticated encryption draws a
    /// fresh nonce per message, so an encrypted profile compares semantics instead.
    /// </summary>
    protected virtual bool BytesAreDeterministic => true;

    /// <summary>The key the header-derived overloads need, when the profile encrypts.</summary>
    protected virtual byte[]? Key => null;

    private BinarySerializer Serializer => new(Options);

    private static Person Value => new() { Name = "Alice", Age = 30 };

    private static void AssertIsTheValue(Person? restored)
    {
        Assert.NotNull(restored);
        Assert.Equal("Alice", restored.Name);
        Assert.Equal(30, restored.Age);
    }

    private static byte[] ToBytes(Action<Stream> write)
    {
        using var stream = new MemoryStream();
        write(stream);
        return stream.ToArray();
    }

    // --- XEP-01, XEP-02: each entry point on its own --------------------------------------------

    [Fact]
    public void ByteArray_RoundTripsThroughItsOwnEntryPoint()
    {
        var serializer = Serializer;

        AssertIsTheValue(serializer.Deserialize<Person>(serializer.Serialize(Value)));
    }

    [Fact]
    public void Stream_RoundTripsThroughItsOwnEntryPoint()
    {
        var serializer = Serializer;

        using var stream = new MemoryStream();
        serializer.Serialize(stream, Value);
        stream.Position = 0;

        AssertIsTheValue(serializer.Deserialize<Person>(stream));
    }

    [Fact]
    public void ByteArrayAndStream_WriteTheSamePayload()
    {
        var serializer = Serializer;

        byte[] fromArray = serializer.Serialize(Value);
        byte[] fromStream = ToBytes(stream => serializer.Serialize(stream, Value));

        if (BytesAreDeterministic)
            Assert.Equal(fromArray, fromStream);

        AssertIsTheValue(serializer.Deserialize<Person>(new MemoryStream(fromArray)));
        AssertIsTheValue(serializer.Deserialize<Person>(fromStream));
    }

    [Fact]
    public void ByteArrayAndStream_ReadEachOthersPayloads()
    {
        var serializer = Serializer;

        using var written = new MemoryStream();
        serializer.Serialize(written, Value);
        written.Position = 0;

        AssertIsTheValue(serializer.Deserialize<Person>(written));
        AssertIsTheValue(serializer.Deserialize<Person>(serializer.Serialize(Value)));
    }

    // --- XEP-03: the stream extensions ----------------------------------------------------------

    [Fact]
    public void StreamExtensions_WithOptions_AgreeWithTheSerializer()
    {
        byte[] throughExtension = ToBytes(stream => stream.Serialize(Value, Options));

        if (BytesAreDeterministic)
            Assert.Equal(Serializer.Serialize(Value), throughExtension);

        using var source = new MemoryStream(throughExtension);
        AssertIsTheValue(source.Deserialize<Person>(Options));
    }

    [Fact]
    public void StreamExtensions_WithOptions_ReadWhatTheSerializerWrote()
    {
        using var source = new MemoryStream(Serializer.Serialize(Value));

        AssertIsTheValue(source.Deserialize<Person>(Options));
    }

    [Fact]
    public void Serializer_ReadsWhatTheStreamExtensionWrote()
    {
        using var source = new MemoryStream(ToBytes(stream => stream.Serialize(Value, Options)));

        AssertIsTheValue(Serializer.Deserialize<Person>(source));
    }

    [Fact]
    public void StreamExtensions_ConfiguredFromTheHeader_ReadWhatTheSerializerWrote()
    {
        if (!SelfDescribing)
        {
            // A headerless payload names no algorithms, so these overloads have nothing to read and
            // say so rather than guessing (section 10.2).
            using var headerless = new MemoryStream(Serializer.Serialize(Value));
            Assert.Throws<BinaryFormatException>(() => headerless.Deserialize<Person>(Key));
            return;
        }

        using var source = new MemoryStream(Serializer.Serialize(Value));
        AssertIsTheValue(source.Deserialize<Person>(Key));

        source.Position = 0;
        AssertIsTheValue(source.Deserialize<Person>(_ => Key));
    }

    // --- XEP-04: the existing-instance overloads ------------------------------------------------

    [Fact]
    public void ExistingInstance_OverloadsAgreeAndReturnTheTargetTheyWereGiven()
    {
        var serializer = Serializer;
        byte[] payload = serializer.Serialize(Value);

        var fromArray = new Person();
        var fromStream = new Person();
        var fromExtension = new Person();

        object? returnedFromArray = serializer.Deserialize(payload, fromArray);

        using (var source = new MemoryStream(payload))
            serializer.Deserialize(source, fromStream);

        using (var source = new MemoryStream(payload))
            source.Deserialize(fromExtension, Options);

        Assert.Same(fromArray, returnedFromArray);
        AssertIsTheValue(fromArray);
        AssertIsTheValue(fromStream);
        AssertIsTheValue(fromExtension);
    }

    // --- XEP-05: the ref value-type overloads ---------------------------------------------------

    [Fact]
    public void RefValueType_OverloadsAgree()
    {
        var serializer = Serializer;
        byte[] payload = serializer.Serialize(new PointStruct { X = 3, Y = 4 });

        var fromArray = default(PointStruct);
        var fromStream = default(PointStruct);
        var fromExtension = default(PointStruct);

        serializer.Deserialize(payload, ref fromArray);

        using (var source = new MemoryStream(payload))
            serializer.Deserialize(source, ref fromStream);

        using (var source = new MemoryStream(payload))
            source.Deserialize(ref fromExtension, Options);

        Assert.Equal(new PointStruct { X = 3, Y = 4 }, fromArray);
        Assert.Equal(fromArray, fromStream);
        Assert.Equal(fromArray, fromExtension);
    }

    // --- every entry point leaves the caller's stream open --------------------------------------

    [Fact]
    public void EveryStreamEntryPoint_LeavesTheCallersStreamOpen()
    {
        var serializer = Serializer;

        using var destination = new TrackingStream();
        serializer.Serialize(destination, Value);
        Assert.False(destination.Disposed);

        using var source = new TrackingStream(serializer.Serialize(Value));
        serializer.Deserialize<Person>(source);
        Assert.False(source.Disposed);

        using var extension = new TrackingStream(serializer.Serialize(Value));
        extension.Deserialize<Person>(Options);
        Assert.False(extension.Disposed);
    }
}

/// <summary>XEP-01 to XEP-05 under profile P0, the default V1 frame.</summary>
public sealed class DefaultCrossEntryPointTests : CrossEntryPoint
{
    protected override BinarySerializerOptions Options { get; } = BinarySerializerOptions.Default;
}

/// <summary>
/// XEP-06, profile P6: the same agreement under the full V1 envelope. The nonce makes two writes of
/// one value differ, so parity here is parity of what comes back, never of ciphertext bytes
/// (section 22.6).
/// </summary>
public sealed class ProtectedCrossEntryPointTests : CrossEntryPoint
{
    private static readonly byte[] Material =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    protected override BinarySerializerOptions Options { get; } = BinarySerializerOptions.Configure()
        .WithCompression(new Brotli())
        .WithChecksum(new Crc32())
        .WithEncryption(new Aes256Gcm(), Material, keyId: "primary")
        .Build();

    protected override bool BytesAreDeterministic => false;

    protected override byte[]? Key => Material;
}

/// <summary>
/// XEP-07, profile P7: the same agreement under the headerless format, for every entry point V0
/// supports. The header-derived overloads are the exception, and the suite asserts what they do
/// instead of skipping them (section 10.2).
/// </summary>
public sealed class HeaderlessCrossEntryPointTests : CrossEntryPoint
{
    protected override BinarySerializerOptions Options { get; } = BinarySerializerOptions.Configure()
        .WithVersion(0)
        .AllowV0Fallback()
        .Build();

    protected override bool SelfDescribing => false;
}
