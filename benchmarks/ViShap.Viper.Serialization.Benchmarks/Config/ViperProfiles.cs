using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Config;

/// <summary>
/// The configuration profiles of Benchmark-Plan §8. Each one is a configuration a consumer can build,
/// and every measurement names the profile it belongs to.
/// </summary>
public enum ViperProfile
{
    /// <summary>B-P0 — V1, no algorithms.</summary>
    Default,

    /// <summary>B-P1 — V1 with reference framing.</summary>
    PreserveReferences,

    /// <summary>B-P2 — V1 under a tight limit policy.</summary>
    TightLimits,

    /// <summary>B-P3 — V1 with Deflate.</summary>
    Deflate,

    /// <summary>B-P3 — V1 with Brotli.</summary>
    Brotli,

    /// <summary>B-P4 — V1 with CRC-32.</summary>
    Crc32,

    /// <summary>B-P5 — V1 with AES-256-GCM.</summary>
    Aes256Gcm,

    /// <summary>B-P6 — V1 with Brotli, CRC-32 and AES-256-GCM.</summary>
    ProtectedBrotli,

    /// <summary>B-P6 — V1 with Deflate, CRC-32 and AES-256-GCM.</summary>
    ProtectedDeflate,

    /// <summary>B-P7 — the headerless compact codec.</summary>
    Headerless,
}

public static class ViperProfiles
{
    /// <summary>The fixed key every encrypted profile uses, so a payload is reproducible across runs.</summary>
    private static readonly byte[] Key =
    [
        0x1F, 0x2E, 0x3D, 0x4C, 0x5B, 0x6A, 0x79, 0x88,
        0x97, 0xA6, 0xB5, 0xC4, 0xD3, 0xE2, 0xF1, 0x00,
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x01,
    ];

    /// <summary>
    /// The tight policy of B-P2. Every ceiling is far below the corpus it will carry, but high enough
    /// that the datasets of §9 still fit: the profile measures what accounting costs, not what a
    /// breach costs.
    /// </summary>
    private static readonly SerializationLimits Tight = SerializationLimits.Default with
    {
        MaxDepth = 600,
        MaxArrayLength = 950_000,
        MaxCollectionLength = 200_000,
        MaxDictionaryEntries = 200_000,
        MaxStringBytes = 200_000,
        MaxByteBlobBytes = 1_000_000,
        MaxTotalElements = 5_000_000,
        MaxObjectGraphNodes = 500_000,
        MaxKeyedFields = 1_000,
        MaxTotalKeyedFields = 200_000,
    };

    internal static IReadOnlyList<ViperProfile> All { get; } = Enum.GetValues<ViperProfile>();

    /// <summary>The plan's identifier for a profile, as it appears in a published cell.</summary>
    internal static string PlanId(ViperProfile profile) => profile switch
    {
        ViperProfile.Default => "B-P0",
        ViperProfile.PreserveReferences => "B-P1",
        ViperProfile.TightLimits => "B-P2",
        ViperProfile.Deflate => "B-P3d",
        ViperProfile.Brotli => "B-P3b",
        ViperProfile.Crc32 => "B-P4",
        ViperProfile.Aes256Gcm => "B-P5",
        ViperProfile.ProtectedBrotli => "B-P6b",
        ViperProfile.ProtectedDeflate => "B-P6d",
        ViperProfile.Headerless => "B-P7",
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    internal static BinarySerializerOptions Options(ViperProfile profile) => profile switch
    {
        ViperProfile.Default =>
            BinarySerializerOptions.Configure().Build(),

        ViperProfile.PreserveReferences =>
            BinarySerializerOptions.Configure().PreserveReferences().Build(),

        ViperProfile.TightLimits =>
            BinarySerializerOptions.Configure().WithLimits(Tight).Build(),

        ViperProfile.Deflate =>
            BinarySerializerOptions.Configure().WithCompression(new Deflate()).Build(),

        ViperProfile.Brotli =>
            BinarySerializerOptions.Configure().WithCompression(new Brotli()).Build(),

        ViperProfile.Crc32 =>
            BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build(),

        ViperProfile.Aes256Gcm =>
            BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), Key, "bench").Build(),

        ViperProfile.ProtectedBrotli =>
            BinarySerializerOptions.Configure()
                .WithCompression(new Brotli())
                .WithChecksum(new Crc32())
                .WithEncryption(new Aes256Gcm(), Key, "bench")
                .Build(),

        ViperProfile.ProtectedDeflate =>
            BinarySerializerOptions.Configure()
                .WithCompression(new Deflate())
                .WithChecksum(new Crc32())
                .WithEncryption(new Aes256Gcm(), Key, "bench")
                .Build(),

        ViperProfile.Headerless =>
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(),

        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
