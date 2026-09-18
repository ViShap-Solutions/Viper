using System.Reflection;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins EXT-01 and EXT-05: the compiled public surface is exactly the one contract §3 lists. A type
/// that appears here without appearing there is an unannounced API addition; one that disappears is a
/// break. Either way the contract and the assembly must be changed together.
/// </summary>
public class PublicSurfaceTests
{
    /// <summary>Every public type contract §3 declares, by namespace-qualified name.</summary>
    private static readonly string[] Documented =
    [
        "ViShap.Viper.BinarySerializer",
        "ViShap.Viper.BinarySerializerOptions",
        "ViShap.Viper.BinarySerializerOptionsBuilder",
        "ViShap.Viper.StreamExtensions",
        "ViShap.Viper.BinaryContractAttribute",
        "ViShap.Viper.BinaryKeyAttribute",
        "ViShap.Viper.BinaryIgnoreAttribute",
        "ViShap.Viper.BinaryIncludeAttribute",
        "ViShap.Viper.BinaryOrderAttribute",
        "ViShap.Viper.BinaryUnionAttribute",

        "ViShap.Viper.Security.SerializationLimits",

        "ViShap.Viper.Compression.CompressionAlgorithm",
        "ViShap.Viper.Compression.ICompressionAlgorithm",
        "ViShap.Viper.Compression.NoCompression",
        "ViShap.Viper.Compression.Deflate",
        "ViShap.Viper.Compression.Brotli",

        "ViShap.Viper.Checksum.ChecksumAlgorithm",
        "ViShap.Viper.Checksum.IChecksumAlgorithm",
        "ViShap.Viper.Checksum.NoChecksum",
        "ViShap.Viper.Checksum.Crc32",

        "ViShap.Viper.Crypto.EncryptionAlgorithm",
        "ViShap.Viper.Crypto.IEncryptionAlgorithm",
        "ViShap.Viper.Crypto.NoEncryption",
        "ViShap.Viper.Crypto.Aes256Gcm",
        "ViShap.Viper.Crypto.SecretKey",
        "ViShap.Viper.Crypto.IKeyProvider",
        "ViShap.Viper.Crypto.StaticKeyProvider",
        "ViShap.Viper.Crypto.DelegateKeyProvider",

        "ViShap.Viper.Metadata.BinaryHeaderInfo",
        "ViShap.Viper.Metadata.BinaryFormatInspector",

        "ViShap.Viper.Diagnostics.BinaryFormatDumper",

        "ViShap.Viper.Exceptions.BinarySerializerException",
        "ViShap.Viper.Exceptions.BinaryConfigurationException",
        "ViShap.Viper.Exceptions.BinaryFormatException",
        "ViShap.Viper.Exceptions.BinaryLimitException",
        "ViShap.Viper.Exceptions.BinaryFormatNotSupportedException",
        "ViShap.Viper.Exceptions.BinaryIntegrityException",
        "ViShap.Viper.Exceptions.BinaryEncryptionException",
        "ViShap.Viper.Exceptions.BinaryEncryptionKeyException",
        "ViShap.Viper.Exceptions.BinaryStreamException",
        "ViShap.Viper.Exceptions.BinaryTypeException"
    ];

    private static string[] ActualSurface() =>
        [.. new[] { typeof(BinarySerializer).Assembly, typeof(Exceptions.BinarySerializerException).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void PublicSurface_ContainsNothingBeyondTheContract()
    {
        string[] undocumented = [.. ActualSurface().Except(Documented, StringComparer.Ordinal)];

        Assert.True(
            undocumented.Length == 0,
            $"Public types absent from System-Contract.md §3: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void PublicSurface_ContainsEverythingTheContractPromises()
    {
        string[] missing = [.. Documented.Except(ActualSurface(), StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0,
            $"Types promised by System-Contract.md §3 but not exported: {string.Join(", ", missing)}");
    }

    [Fact]
    public void PublicSurface_ExposesNoNestedPublicTypes()
    {
        var nested = new[] { typeof(BinarySerializer).Assembly, typeof(Exceptions.BinarySerializerException).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsNested)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(nested);
    }

    [Fact]
    public void ExceptionHierarchy_MatchesTheContract()
    {
        var exceptions = typeof(Exceptions.BinarySerializerException).Assembly
            .GetExportedTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t))
            .ToArray();

        Assert.All(exceptions, type => Assert.True(
            type == typeof(Exceptions.BinarySerializerException) ||
            typeof(Exceptions.BinarySerializerException).IsAssignableFrom(type),
            $"'{type}' does not derive from BinarySerializerException."));

        Assert.True(typeof(Exceptions.BinaryFormatException)
            .IsAssignableFrom(typeof(Exceptions.BinaryLimitException)));
        Assert.True(typeof(Exceptions.BinaryEncryptionException)
            .IsAssignableFrom(typeof(Exceptions.BinaryEncryptionKeyException)));
    }

    [Fact]
    public void EngineTypes_AreNotPublic()
    {
        // Each of these enforces part of the resource policy or the traversal protocol; publishing
        // any would let a caller step around it.
        string[] internalNames =
        [
            "ViShap.Viper.Engine.GraphReader",
            "ViShap.Viper.Engine.GraphWriter",
            "ViShap.Viper.Engine.TypeContract",
            "ViShap.Viper.Io.ValueReader",
            "ViShap.Viper.Io.ValueWriter",
            "ViShap.Viper.Io.ElementCount",
            "ViShap.Viper.Security.SerializationBudget",
            "ViShap.Viper.Security.SerializationOperation",
            "ViShap.Viper.Security.MeteredReadStream",
            "ViShap.Viper.Formatters.ITypeFormatter",
            "ViShap.Viper.Pipeline.FormatRouter"
        ];

        var exported = ActualSurface();

        Assert.All(internalNames, name => Assert.DoesNotContain(name, exported));
    }
}
