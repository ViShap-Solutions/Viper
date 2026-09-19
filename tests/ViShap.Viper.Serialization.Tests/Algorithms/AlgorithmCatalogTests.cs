using System.Collections.Concurrent;
using System.Reflection;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Configuration;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins CAT-01…CAT-08: what a payload header names is resolved from the options that were built, and
/// from nothing else. Built-in identifiers are fixed, a custom name reaches only the configuration
/// that registered it, and the set of registrations is a snapshot taken by <c>Build()</c>.
/// </summary>
public class AlgorithmCatalogTests
{
    private const BindingFlags AllDeclaredStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // --- CAT-01: the built-in identifiers ----------------------------------------------------------

    [Fact]
    public void Resolve_ADefinedBuiltInIdentifier_YieldsTheAlgorithmItNames()
    {
        var catalog = AlgorithmCatalog.BuiltIn;

        Assert.IsType<NoCompression>(catalog.ResolveCompression(CompressionAlgorithm.None, null));
        Assert.IsType<Deflate>(catalog.ResolveCompression(CompressionAlgorithm.Deflate, null));
        Assert.IsType<Brotli>(catalog.ResolveCompression(CompressionAlgorithm.Brotli, null));

        Assert.IsType<NoChecksum>(catalog.ResolveChecksum(ChecksumAlgorithm.None, null));
        Assert.IsType<Crc32>(catalog.ResolveChecksum(ChecksumAlgorithm.Crc32, null));

        Assert.IsType<NoEncryption>(catalog.ResolveEncryption(EncryptionAlgorithm.None, null));
        Assert.IsType<Aes256Gcm>(catalog.ResolveEncryption(EncryptionAlgorithm.Aes256Gcm, null));
    }

    [Fact]
    public void Resolve_AnIdentifierNoBuiltInDefines_ThrowsFormatNotSupported()
    {
        var catalog = AlgorithmCatalog.BuiltIn;

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => catalog.ResolveCompression((CompressionAlgorithm)7, null));
        Assert.Throws<BinaryFormatNotSupportedException>(
            () => catalog.ResolveChecksum((ChecksumAlgorithm)7, null));
        Assert.Throws<BinaryFormatNotSupportedException>(
            () => catalog.ResolveEncryption((EncryptionAlgorithm)7, null));
    }

    // --- CAT-02, CAT-03: custom names come from the options that registered them --------------------

    [Fact]
    public void Resolve_ARegisteredCustomName_ComesFromTheOptionsCatalog()
    {
        var options = BinarySerializerOptions.Configure()
            .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8())
            .RegisterCustomCompression(
                IdentityCompression.RegisteredName, static () => new IdentityCompression())
            .Build();

        Assert.IsType<Sum8>(
            options.Catalog.ResolveChecksum(ChecksumAlgorithm.Custom, Sum8.RegisteredName));
        Assert.IsType<IdentityCompression>(
            options.Catalog.ResolveCompression(
                CompressionAlgorithm.Custom, IdentityCompression.RegisteredName));
    }

    [Fact]
    public void Resolve_ACustomNameNobodyRegistered_ThrowsFormatNotSupported()
    {
        var catalog = BinarySerializerOptions.Configure()
            .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8())
            .Build()
            .Catalog;

        AssertEx.Throws<BinaryFormatNotSupportedException>(
            "unheard-of", () => catalog.ResolveChecksum(ChecksumAlgorithm.Custom, "unheard-of"));
    }

    [Fact]
    public void Resolve_ACustomIdentifierWithNoNameAtAll_ThrowsFormatNotSupported()
    {
        Assert.Throws<BinaryFormatNotSupportedException>(
            () => AlgorithmCatalog.BuiltIn.ResolveEncryption(EncryptionAlgorithm.Custom, null));
    }

    // --- CAT-04, CAT-05: the catalog is a snapshot, one per configuration ---------------------------

    [Fact]
    public void Build_TakesASnapshot_SoARegistrationMadeAfterwardsDoesNotReachIt()
    {
        var builder = BinarySerializerOptions.Configure()
            .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8());

        var before = builder.Build();
        builder.RegisterCustomChecksum(WideChecksum.RegisteredName, static () => new WideChecksum(2));
        var after = builder.Build();

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => before.Catalog.ResolveChecksum(ChecksumAlgorithm.Custom, WideChecksum.RegisteredName));
        Assert.IsType<WideChecksum>(
            after.Catalog.ResolveChecksum(ChecksumAlgorithm.Custom, WideChecksum.RegisteredName));
    }

    [Fact]
    public void Catalogs_OfTwoConfigurations_AreIndependent()
    {
        var checksums = BinarySerializerOptions.Configure()
            .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8())
            .Build();

        var compressions = BinarySerializerOptions.Configure()
            .RegisterCustomCompression(
                IdentityCompression.RegisteredName, static () => new IdentityCompression())
            .Build();

        Assert.IsType<Sum8>(
            checksums.Catalog.ResolveChecksum(ChecksumAlgorithm.Custom, Sum8.RegisteredName));
        Assert.Throws<BinaryFormatNotSupportedException>(
            () => checksums.Catalog.ResolveCompression(
                CompressionAlgorithm.Custom, IdentityCompression.RegisteredName));

        Assert.IsType<IdentityCompression>(
            compressions.Catalog.ResolveCompression(
                CompressionAlgorithm.Custom, IdentityCompression.RegisteredName));
        Assert.Throws<BinaryFormatNotSupportedException>(
            () => compressions.Catalog.ResolveChecksum(ChecksumAlgorithm.Custom, Sum8.RegisteredName));
    }

    // --- CAT-06: nothing process-wide can substitute an algorithm ----------------------------------

    [Fact]
    public void Catalog_HoldsNoMutableStaticState()
    {
        var mutable = typeof(AlgorithmCatalog)
            .GetFields(AllDeclaredStatic)
            .Where(field => !field.IsInitOnly && !field.IsLiteral)
            .Select(field => field.Name)
            .ToArray();

        Assert.True(mutable.Length == 0, $"A static field can be reassigned: {string.Join(", ", mutable)}");
    }

    [Fact]
    public void Catalog_ExposesNoStaticMemberThatChangesOne()
    {
        // Everything the engine can reach statically produces a catalog; none of it edits one, so a
        // registration can enter only through the builder that made the options.
        var reachable = typeof(AlgorithmCatalog)
            .GetMethods(AllDeclaredStatic)
            .Where(method => !method.IsPrivate)
            .ToArray();

        Assert.NotEmpty(reachable);
        Assert.All(reachable, method => Assert.Equal(typeof(AlgorithmCatalog), method.ReturnType));
    }

    // --- CAT-07: a factory runs per resolution -----------------------------------------------------

    [Fact]
    public void Deserialize_ACustomAlgorithm_RunsItsFactoryOncePerResolution()
    {
        int created = 0;
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Sum8())
                .RegisterCustomChecksum(Sum8.RegisteredName, () => { created++; return new Sum8(); })
                .Build());

        byte[] frame = serializer.Serialize(123);

        // Writing uses the configured instance; the catalog answers the header, so only reads resolve.
        Assert.Equal(0, created);

        serializer.Deserialize<int>(frame);
        serializer.Deserialize<int>(frame);

        Assert.Equal(2, created);
    }

    // --- CAT-09: a factory that fails stays inside the taxonomy ------------------------------------

    [Fact]
    public void Deserialize_ARegisteredFactoryThatThrows_ThrowsConfigurationPreservingTheCause()
    {
        // The header names the factory, so a payload decides which one runs: whatever it raises must
        // still reach the caller as something a `catch (BinarySerializerException)` holds (§4.1, §9).
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .RegisterCustomChecksum(
                    Sum8.RegisteredName,
                    static IChecksumAlgorithm () =>
                        throw new InvalidOperationException("the key ring is offline"))
                .Build());

        var error = AssertEx.Throws<BinaryConfigurationException>(
            Sum8.RegisteredName, () => serializer.Deserialize<int>(CustomChecksumFrame()));

        var cause = Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal("the key ring is offline", cause.Message);
    }

    [Fact]
    public void Deserialize_ARegisteredFactoryThatReturnsNull_ThrowsConfiguration()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .RegisterCustomChecksum(Sum8.RegisteredName, static () => null!)
                .Build());

        AssertEx.Throws<BinaryConfigurationException>(
            "returned null", () => serializer.Deserialize<int>(CustomChecksumFrame()));
    }

    /// <summary>A frame whose header names the custom checksum, so reading one resolves the factory.</summary>
    private static byte[] CustomChecksumFrame() =>
        new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Sum8())
                .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8())
                .Build()).Serialize(123);

    // --- CAT-08: one catalog serves concurrent operations ------------------------------------------

    [Fact]
    public void Deserialize_FromManyThreadsAtOnce_ResolvesOneCatalogSafely()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Sum8())
                .RegisterCustomChecksum(Sum8.RegisteredName, static () => new Sum8())
                .WithCompression(new IdentityCompression())
                .RegisterCustomCompression(
                    IdentityCompression.RegisteredName, static () => new IdentityCompression())
                .Build());

        var source = new Person { Name = "Alice", Age = 30 };
        byte[] frame = serializer.Serialize(source);

        var restored = new ConcurrentBag<string>();
        Parallel.For(
            0,
            256,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            _ => restored.Add(serializer.Deserialize<Person>(frame)!.Name));

        Assert.Equal(256, restored.Count);
        Assert.All(restored, name => Assert.Equal("Alice", name));
    }
}
