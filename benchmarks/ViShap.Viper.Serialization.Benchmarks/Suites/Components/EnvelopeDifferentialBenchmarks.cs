using BenchmarkDotNet.Attributes;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>One configuration of the envelope, differing from its neighbour in exactly one field.</summary>
public enum EnvelopeConfiguration
{
    /// <summary>V0: a bare payload with no header at all.</summary>
    Headerless,

    /// <summary>V1 with no algorithms: the narrowest header the format admits.</summary>
    MinimalHeader,

    /// <summary>V1 encrypted, with no key id in the header.</summary>
    Encrypted,

    /// <summary>V1 encrypted, with a key id. Differs from <see cref="Encrypted"/> in that field alone.</summary>
    EncryptedWithKeyId,

    /// <summary>V1 compressed, checksummed and encrypted, all three by a known kind, with a key id.</summary>
    Protected,

    /// <summary>
    /// The same three algorithms doing the same work, declared as custom ones. Differs from
    /// <see cref="Protected"/> in the three name strings the header carries and nothing else.
    /// </summary>
    ProtectedCustomNames,
}

/// <summary>
/// DIFF-01 — the V1 envelope by difference, on one value, through the public surface.
/// </summary>
/// <remarks>
/// Six configurations, each one field away from its neighbour, so what a subtraction isolates is named
/// rather than assumed: <c>MinimalHeader</c> minus <c>Headerless</c> is the envelope itself,
/// <c>EncryptedWithKeyId</c> minus <c>Encrypted</c> is the key id, and <c>ProtectedCustomNames</c> minus
/// <c>Protected</c> is the three custom name strings. Cross-checks MICRO-08, which measures the same
/// header directly: the two must agree within their combined margins, and DIFF-07 is where that is
/// settled before either is published.
/// </remarks>
[MemoryDiagnoser]
public class EnvelopeDifferentialBenchmarks
{
    private const string KeyId = "benchmark-key-2026-09";

    private static readonly byte[] Key =
    [
        0x1F, 0x2E, 0x3D, 0x4C, 0x5B, 0x6A, 0x79, 0x88,
        0x97, 0xA6, 0xB5, 0xC4, 0xD3, 0xE2, 0xF1, 0x00,
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x01,
    ];

    private BinarySerializer _serializer = null!;
    private MediumObject _value = null!;
    private byte[] _payload = [];

    [Params(
        EnvelopeConfiguration.Headerless,
        EnvelopeConfiguration.MinimalHeader,
        EnvelopeConfiguration.Encrypted,
        EnvelopeConfiguration.EncryptedWithKeyId,
        EnvelopeConfiguration.Protected,
        EnvelopeConfiguration.ProtectedCustomNames)]
    public EnvelopeConfiguration Configuration { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _serializer = new BinarySerializer(Options(Configuration));
        _value = (MediumObject)Corpus.Find("DATA-02").BoxedValue;
        _payload = _serializer.Serialize(_value);
    }

    [Benchmark(Description = "DIFF-01 serialize")]
    public byte[] Serialize() => _serializer.Serialize(_value);

    [Benchmark(Description = "DIFF-01 deserialize")]
    public object? Deserialize() => _serializer.Deserialize<MediumObject>(_payload);

    private static BinarySerializerOptions Options(EnvelopeConfiguration configuration) =>
        configuration switch
        {
            EnvelopeConfiguration.Headerless =>
                BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(),

            EnvelopeConfiguration.MinimalHeader =>
                BinarySerializerOptions.Configure().Build(),

            EnvelopeConfiguration.Encrypted =>
                BinarySerializerOptions.Configure()
                    .WithEncryption(new Aes256Gcm(), Key)
                    .Build(),

            EnvelopeConfiguration.EncryptedWithKeyId =>
                BinarySerializerOptions.Configure()
                    .WithEncryption(new Aes256Gcm(), Key, KeyId)
                    .Build(),

            EnvelopeConfiguration.Protected =>
                BinarySerializerOptions.Configure()
                    .WithCompression(new Deflate())
                    .WithChecksum(new Crc32())
                    .WithEncryption(new Aes256Gcm(), Key, KeyId)
                    .Build(),

            EnvelopeConfiguration.ProtectedCustomNames =>
                BinarySerializerOptions.Configure()
                    .WithCompression(new CustomNamedAlgorithms.NamedDeflate())
                    .WithChecksum(new CustomNamedAlgorithms.NamedCrc32())
                    .WithEncryption(new CustomNamedAlgorithms.NamedAes256Gcm(), Key, KeyId)
                    .RegisterCustomCompression(
                        CustomNamedAlgorithms.CompressionName,
                        () => new CustomNamedAlgorithms.NamedDeflate())
                    .RegisterCustomChecksum(
                        CustomNamedAlgorithms.ChecksumName,
                        () => new CustomNamedAlgorithms.NamedCrc32())
                    .RegisterCustomEncryption(
                        CustomNamedAlgorithms.EncryptionName,
                        () => new CustomNamedAlgorithms.NamedAes256Gcm())
                    .Build(),

            _ => throw new ArgumentOutOfRangeException(nameof(configuration)),
        };
}
