using System.Reflection;
using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Exceptions;

/// <summary>
/// Pins CFG-01…CFG-04 and CFG-06…CFG-10: a configuration value of zero or less is a
/// configuration error, the default policy matches the §5 table, and a contradictory combination is
/// refused when the options are built.
/// </summary>
public class ConfigurationValidationTests
{
    /// <summary>
    /// Every limit, discovered from the type rather than listed, so a limit added later is covered
    /// without anyone remembering to add it here.
    /// </summary>
    public static TheoryData<string> LimitNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in NumericLimits().Select(p => p.Name))
                data.Add(name);

            return data;
        }
    }

    private static PropertyInfo[] NumericLimits() =>
        [.. typeof(SerializationLimits)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(int) || p.PropertyType == typeof(long))];

    private static SerializationLimits WithValue(string name, long value)
    {
        // A fresh copy: nothing here may touch the shared default instance.
        var limits = SerializationLimits.Default with { };
        var property = typeof(SerializationLimits).GetProperty(name)!;
        property.SetValue(
            limits,
            property.PropertyType == typeof(int) ? (object)(int)value : value);

        return limits;
    }

    [Fact]
    public void EveryLimit_IsDiscoveredByTheTheory()
    {
        // Guards the theories below: if discovery broke, they would silently cover nothing.
        Assert.Equal(15, NumericLimits().Length);
    }

    [Theory]
    [MemberData(nameof(LimitNames))]
    public void Validate_LimitAtZero_ThrowsConfigurationNamingIt(string name)
    {
        var error = Assert.Throws<BinaryConfigurationException>(() => WithValue(name, 0).Validate());

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LimitNames))]
    public void Validate_NegativeLimit_ThrowsConfigurationNamingIt(string name)
    {
        var error = Assert.Throws<BinaryConfigurationException>(() => WithValue(name, -1).Validate());

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LimitNames))]
    public void Build_LimitAtZero_ThrowsConfiguration(string name)
    {
        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure().WithLimits(WithValue(name, 0)).Build());
    }

    [Fact]
    public void Validate_AValidPositivePolicy_Succeeds()
    {
        (SerializationLimits.Default with { MaxDepth = 1, MaxStringBytes = 1 }).Validate();
    }

    /// <summary>The §5 table of the contract, limit by limit.</summary>
    private static readonly Dictionary<string, long> DocumentedDefaults = new()
    {
        [nameof(SerializationLimits.MaxDepth)] = 512,
        [nameof(SerializationLimits.MaxArrayLength)] = 1_000_000,
        [nameof(SerializationLimits.MaxCollectionLength)] = 1_000_000,
        [nameof(SerializationLimits.MaxDictionaryEntries)] = 1_000_000,
        [nameof(SerializationLimits.MaxStringBytes)] = 4_000_000,
        [nameof(SerializationLimits.MaxByteBlobBytes)] = 16_000_000,
        [nameof(SerializationLimits.MaxTotalElements)] = 10_000_000,
        [nameof(SerializationLimits.MaxObjectGraphNodes)] = 1_000_000,
        [nameof(SerializationLimits.MaxKeyedFields)] = 1_000_000,
        [nameof(SerializationLimits.MaxTotalKeyedFields)] = 10_000_000,
        [nameof(SerializationLimits.MaxPayloadBytes)] = 64L * 1024 * 1024,
        [nameof(SerializationLimits.MaxCompressedBytes)] = 64L * 1024 * 1024,
        [nameof(SerializationLimits.MaxDecompressionRatio)] = 10_000,
        [nameof(SerializationLimits.MaxEncryptedBytes)] = 64L * 1024 * 1024 + 64L * 1024,
        [nameof(SerializationLimits.MaxWireBytes)] = 80L * 1024 * 1024
    };

    [Fact]
    public void Default_MatchesTheDocumentedTable()
    {
        foreach (var limit in NumericLimits())
            Assert.Equal(
                DocumentedDefaults[limit.Name],
                Convert.ToInt64(limit.GetValue(SerializationLimits.Default)));
    }

    [Fact]
    public void DocumentedTable_CoversEveryLimit()
    {
        // A limit added to the type without a row here would otherwise have no pinned default: the
        // table and the type must name exactly the same set.
        Assert.Equal(
            DocumentedDefaults.Keys.Order(StringComparer.Ordinal),
            NumericLimits().Select(limit => limit.Name).Order(StringComparer.Ordinal));
    }

    // --- contradictory combinations ------------------------------------------------------------------

    [Fact]
    public void Build_RequireEncryptionWithoutAnAlgorithm_ThrowsConfiguration()
    {
        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure().RequireEncryption().Build());
    }

    [Fact]
    public void Build_RequireEncryptionWithAnAlgorithmThatCannotAuthenticateMetadata_ThrowsConfiguration()
    {
        var builder = BinarySerializerOptions.Configure()
            .WithEncryption(new UnauthenticatedCipher(), RandomNumberGenerator.GetBytes(32))
            .RequireEncryption();

        AssertEx.Throws<BinaryConfigurationException>("authenticate", () => builder.Build());
    }

    [Fact]
    public void WithEncryption_HasNoOverloadThatLeavesKeyMaterialUnset()
    {
        // CFG-07. §4.1 puts missing key material outside the contradictions `Build()` rejects,
        // because it is unreachable: every overload takes the key source in the same call as the
        // algorithm. That is the property asserted here — an algorithm-only overload added later
        // would create the configuration §4.1 says cannot exist, and this fails.
        var overloads = typeof(BinarySerializerOptionsBuilder)
            .GetMethods()
            .Where(method => method.Name == nameof(BinarySerializerOptionsBuilder.WithEncryption))
            .ToArray();

        Assert.Equal(3, overloads.Length);
        Assert.All(overloads, method => Assert.Equal(3, method.GetParameters().Length));
        Assert.All(
            overloads,
            method => Assert.Equal(
                1,
                method.GetParameters()
                    .Count(p => p.ParameterType == typeof(ReadOnlySpan<byte>)
                                || p.ParameterType == typeof(IKeyProvider)
                                || p.ParameterType == typeof(Func<string?, byte[]?>))));
    }

    [Fact]
    public void WithEncryption_ANullKeySource_ThrowsArgumentNullLeavingTheBuilderUnchanged()
    {
        // The other half of CFG-07: the overloads exist, and none of them accepts nothing.
        var builder = BinarySerializerOptions.Configure();

        Assert.Throws<ArgumentNullException>(
            () => builder.WithEncryption(new Aes256Gcm(), (IKeyProvider)null!));
        Assert.Throws<ArgumentNullException>(
            () => builder.WithEncryption(new Aes256Gcm(), (Func<string?, byte[]?>)null!));

        Assert.Equal(EncryptionAlgorithm.None, builder.Build().Encryption.Kind);
    }

    [Fact]
    public void Build_RequireChecksumWithoutAChecksumAlgorithm_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "RequireChecksum",
            () => BinarySerializerOptions.Configure().RequireChecksum().Build());
    }

    [Fact]
    public void Build_RequireChecksumWithAChecksumAlgorithm_Succeeds()
    {
        var options = BinarySerializerOptions.Configure()
            .WithChecksum(new Crc32())
            .RequireChecksum()
            .Build();

        Assert.True(options.RequireChecksum);
    }

    [Fact]
    public void Build_RequireEncryptionWithAnAeadAlgorithm_Succeeds()
    {
        var options = BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), RandomNumberGenerator.GetBytes(32))
            .RequireEncryption()
            .Build();

        Assert.True(options.RequireEncryption);
    }
}
