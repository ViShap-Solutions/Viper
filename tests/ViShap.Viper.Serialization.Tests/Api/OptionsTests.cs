using System.Reflection;
using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins OPT-01…OPT-09 and OPT-11…OPT-21: the builder is the only construction path, what it records
/// is what the serializer uses, and a registration reaches exactly one configuration.
/// </summary>
public class OptionsTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    // --- defaults and immutability -----------------------------------------------------------------

    [Fact]
    public void Default_ExposesTheDocumentedDefaults()
    {
        var options = BinarySerializerOptions.Default;

        Assert.Equal(CompressionAlgorithm.None, options.Compression.Kind);
        Assert.Equal(ChecksumAlgorithm.None, options.Checksum.Kind);
        Assert.Equal(EncryptionAlgorithm.None, options.Encryption.Kind);
        Assert.Equal(1, options.WriteVersion);
        Assert.False(options.PreserveReferences);
        Assert.False(options.AllowV0Fallback);
        Assert.False(options.RequireEncryption);
        Assert.False(options.RequireChecksum);
        Assert.Null(options.Keys);
        Assert.Null(options.KeyId);
        Assert.Equal(SerializationLimits.Default, options.Limits);
    }

    [Fact]
    public void Configure_Build_IsSemanticallyTheDefault()
    {
        var built = BinarySerializerOptions.Configure().Build();
        var sample = new Person { Name = "Alice", Age = 30 };

        Assert.Equal(
            new BinarySerializer(BinarySerializerOptions.Default).Serialize(sample),
            new BinarySerializer(built).Serialize(sample));
    }

    [Fact]
    public void Options_ExposeNoPublicSetters()
    {
        var settable = typeof(BinarySerializerOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        Assert.Empty(settable);
    }

    [Fact]
    public void Limits_AreARecordWithNoPublicSetters()
    {
        var derived = SerializationLimits.Default with { MaxDepth = 7 };

        Assert.Equal(7, derived.MaxDepth);
        Assert.Equal(SerializationLimits.Default.MaxArrayLength, derived.MaxArrayLength);
        Assert.NotEqual(SerializationLimits.Default, derived);

        var settable = typeof(SerializationLimits)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } method && method.ReturnParameter
                .GetRequiredCustomModifiers()
                .All(m => m != typeof(System.Runtime.CompilerServices.IsExternalInit)))
            .Select(p => p.Name)
            .ToArray();

        Assert.Empty(settable);
    }

    [Fact]
    public void Validate_IsPublicAndRejectsANonPositiveLimit()
    {
        Assert.Throws<BinaryConfigurationException>(
            () => (SerializationLimits.Default with { MaxStringBytes = 0 }).Validate());
    }

    // --- builder recording --------------------------------------------------------------------------

    [Fact]
    public void WithCompressionAndChecksum_AreRecordedInTheOptionsAndTheHeader()
    {
        var options = BinarySerializerOptions.Configure()
            .WithCompression(new Deflate())
            .WithChecksum(new Crc32())
            .Build();

        Assert.Equal(CompressionAlgorithm.Deflate, options.Compression.Kind);
        Assert.Equal(ChecksumAlgorithm.Crc32, options.Checksum.Kind);

        using var stream = new MemoryStream(
            new BinarySerializer(options).Serialize(new Person { Name = "Alice", Age = 30 }));
        var header = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(CompressionAlgorithm.Deflate, header.Compression);
        Assert.Equal(ChecksumAlgorithm.Crc32, header.ChecksumAlgorithm);
    }

    [Fact]
    public void WithEncryption_CopiesTheKeyImmediately()
    {
        byte[] caller = NewKey();
        var options = BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), caller, "primary")
            .Build();

        Array.Clear(caller);

        var serializer = new BinarySerializer(options);
        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));
    }

    [Fact]
    public void WithEncryption_AcceptsAResolver()
    {
        byte[] key = NewKey();
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), _ => key, "primary")
                .Build());

        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));
    }

    [Fact]
    public void WithEncryption_AcceptsAKeyProvider()
    {
        using var provider = new StaticKeyProvider(NewKey(), "primary");
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), provider, "primary")
                .Build());

        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));
    }

    [Fact]
    public void WithLimits_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => BinarySerializerOptions.Configure().WithLimits(null!));
    }

    [Fact]
    public void WithLimits_StoresTheInstanceItWasGiven()
    {
        var limits = SerializationLimits.Default with { MaxDepth = 9 };

        var options = BinarySerializerOptions.Configure().WithLimits(limits).Build();

        Assert.Same(limits, options.Limits);
    }

    [Fact]
    public void PolicyFlags_DefaultToFalseAndFlipWithTheNoArgumentOverload()
    {
        var options = BinarySerializerOptions.Configure()
            .PreserveReferences()
            .RequireChecksum()
            .WithChecksum(new Crc32())
            .Build();

        Assert.True(options.PreserveReferences);
        Assert.True(options.RequireChecksum);
        Assert.False(options.RequireEncryption);
        Assert.False(options.AllowV0Fallback);
    }

    [Fact]
    public void AllowV0Fallback_FlipsWithTheNoArgumentOverload()
    {
        var options = BinarySerializerOptions.Configure().AllowV0Fallback().Build();

        Assert.True(options.AllowV0Fallback);
    }

    // --- protection policies vs the headerless format -------------------------------------------------

    [Fact]
    public void Build_RequireEncryptionWithTheHeaderlessWriteVersion_ThrowsConfiguration()
    {
        var ex = Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), NewKey())
                .RequireEncryption()
                .WithVersion(0)
                .Build());

        Assert.Contains("format version 0 is selected for writing", ex.Message);
    }

    [Fact]
    public void Build_RequireEncryptionWithTheHeaderlessReadFallback_ThrowsConfiguration()
    {
        var ex = Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), NewKey())
                .RequireEncryption()
                .AllowV0Fallback()
                .Build());

        Assert.Contains("AllowV0Fallback", ex.Message);
    }

    [Fact]
    public void Build_RequireChecksumWithTheHeaderlessFormat_ThrowsConfigurationOnBothSides()
    {
        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32()).RequireChecksum().WithVersion(0).Build());

        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32()).RequireChecksum().AllowV0Fallback().Build());
    }

    [Fact]
    public void Build_AlgorithmsWithTheHeaderlessFormatButNoPolicy_IsAccepted()
    {
        // An algorithm is a capability, not a demand, so it is not a contradiction on its own.
        var options = BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), NewKey())
            .WithChecksum(new Crc32())
            .WithVersion(0)
            .AllowV0Fallback()
            .Build();

        Assert.Equal(0, options.WriteVersion);
        Assert.False(options.RequireEncryption);
    }

    [Fact]
    public void Serialize_ConfiguredAlgorithmsUnderTheHeaderlessFormat_LeaveNoTraceInTheBytes()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), NewKey())
            .WithChecksum(new Crc32())
            .WithVersion(0)
            .AllowV0Fallback()
            .Build());

        byte[] plain = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithVersion(0).AllowV0Fallback().Build()).Serialize(12345);

        Assert.Equal(plain, serializer.Serialize(12345));
    }

    // --- custom algorithm registration ----------------------------------------------------------------

    [Fact]
    public void RegisterCustomChecksum_IsUsedByThatConfiguration()
    {
        var serializer = new BinarySerializer(WithCustomChecksum());

        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));
    }

    [Fact]
    public void RegisterCustomChecksum_IsRecordedInTheHeader()
    {
        using var stream = new MemoryStream(new BinarySerializer(WithCustomChecksum()).Serialize(123));

        var header = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(ChecksumAlgorithm.Custom, header.ChecksumAlgorithm);
        Assert.Equal("sum8", header.CustomChecksumName);
    }

    [Fact]
    public void RegisterCustom_DoesNotReachAnotherConfiguration()
    {
        byte[] payload = new BinarySerializer(WithCustomChecksum()).Serialize(123);

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => new BinarySerializer().Deserialize<int>(payload));
    }

    [Fact]
    public void RegisterCustom_CannotSubstituteABuiltIn()
    {
        // A custom name lives in its own space; the built-in identifiers are not addressable by name.
        var options = BinarySerializerOptions.Configure()
            .WithCompression(new Deflate())
            .RegisterCustomCompression("Deflate", static () => new Brotli())
            .Build();

        using var stream = new MemoryStream(new BinarySerializer(options).Serialize(123));
        var header = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Equal(CompressionAlgorithm.Deflate, header.Compression);
        Assert.Null(header.CustomCompressionName);
        Assert.Equal(123, new BinarySerializer().Deserialize<int>(stream.ToArray()));
    }

    [Fact]
    public void RegisterCustom_NullNameOrFactory_ThrowsArgumentNull()
    {
        var builder = BinarySerializerOptions.Configure();

        Assert.Throws<ArgumentNullException>(() => builder.RegisterCustomChecksum(null!, () => new Crc32()));
        Assert.Throws<ArgumentNullException>(() => builder.RegisterCustomChecksum("x", null!));
        Assert.Throws<ArgumentNullException>(() => builder.RegisterCustomCompression(null!, () => new Deflate()));
        Assert.Throws<ArgumentNullException>(() => builder.RegisterCustomEncryption("x", null!));
    }

    // --- FromHeader / FromStream ------------------------------------------------------------------------

    [Fact]
    public void FromHeader_BuildsOptionsMatchingTheMetadata()
    {
        var source = BinarySerializerOptions.Configure()
            .WithCompression(new Brotli())
            .WithChecksum(new Crc32())
            .Build();
        using var stream = new MemoryStream(new BinarySerializer(source).Serialize(123));

        var options = BinarySerializerOptions.FromHeader(BinaryFormatInspector.Peek(stream)!.Value);

        Assert.Equal(CompressionAlgorithm.Brotli, options.Compression.Kind);
        Assert.Equal(ChecksumAlgorithm.Crc32, options.Checksum.Kind);
        Assert.Equal(123, new BinarySerializer(options).Deserialize<int>(stream.ToArray()));
    }

    [Fact]
    public void FromHeader_InvalidLimits_ThrowsConfiguration()
    {
        using var stream = new MemoryStream(new BinarySerializer().Serialize(123));
        var info = BinaryFormatInspector.Peek(stream)!.Value;

        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.FromHeader(
                info, (byte[]?)null, SerializationLimits.Default with { MaxDepth = 0 }));
    }

    [Fact]
    public void FromStream_RestoresThePosition()
    {
        using var stream = new MemoryStream(new BinarySerializer().Serialize(123));
        stream.Position = 0;

        BinarySerializerOptions.FromStream(stream);

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void FromStream_NonSeekableStream_ThrowsNotSupported()
    {
        using var stream = new NonSeekableStream(new BinarySerializer().Serialize(123));

        Assert.Throws<NotSupportedException>(() => BinarySerializerOptions.FromStream(stream));
    }

    [Fact]
    public void FromStream_UnrecognizedBytes_ThrowsFormat()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<BinaryFormatException>(() => BinarySerializerOptions.FromStream(stream));
    }

    [Fact]
    public void FromStream_PassesTheHeaderKeyIdToTheResolver()
    {
        byte[] key = NewKey();
        byte[] payload = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), key, "rotated-2")
                .Build()).Serialize(123);

        string? observed = null;
        using var stream = new MemoryStream(payload);

        var options = BinarySerializerOptions.FromStream(
            stream, id => { observed = id; return key; });

        Assert.Equal(123, new BinarySerializer(options).Deserialize<int>(payload));
        Assert.Equal("rotated-2", observed);
    }

    [Fact]
    public void KeyId_IsASelectorAndNotKeyMaterial()
    {
        // The same id with different key bytes must not decrypt.
        byte[] payload = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), NewKey(), "primary")
                .Build()).Serialize(123);

        var other = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), NewKey(), "primary")
                .Build());

        Assert.Throws<BinaryIntegrityException>(() => other.Deserialize<int>(payload));
    }

    private static BinarySerializerOptions WithCustomChecksum() =>
        BinarySerializerOptions.Configure()
            .WithChecksum(new Sum8())
            .RegisterCustomChecksum("sum8", static () => new Sum8())
            .Build();
}
