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

    /// <summary>B-P3 — V1 with <c>DeflateCompression</c>.</summary>
    Deflate,

    /// <summary>B-P3 — V1 with <c>BrotliCompression</c>.</summary>
    Brotli,

    /// <summary>B-P4 — V1 with <c>Crc32Checksum</c>.</summary>
    Crc32,

    /// <summary>B-P5 — V1 with <c>Aes256GcmEncryption</c>.</summary>
    Aes256Gcm,

    /// <summary>B-P4x3 — V1 with <c>XxHash3Checksum</c>.</summary>
    XxHash3,

    /// <summary>B-P4x128 — V1 with <c>XxHash128Checksum</c>.</summary>
    XxHash128,

    /// <summary>B-P5c — V1 with <c>ChaCha20Poly1305Encryption</c>.</summary>
    ChaCha20Poly1305,

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

    /// <summary>
    /// The profiles that write version 1 frames. A frame declares its length, so only these can be
    /// awaited or read from a stream that cannot seek; B-P7's payload is refused there by design.
    /// </summary>
    internal static IReadOnlyList<ViperProfile> Framed { get; } =
        [.. All.Where(profile => profile != ViperProfile.Headerless)];

    /// <summary>
    /// The profiles an entry-point suite runs over: the default frame, reference framing, one
    /// encryption, the full envelope and V0 — every kind of path a frame takes through an entry point.
    /// What each of the thirteen profiles costs is the profile matrix's to measure, not an entry point's.
    /// </summary>
    internal static IReadOnlyList<ViperProfile> Representative { get; } =
    [
        ViperProfile.Default,
        ViperProfile.PreserveReferences,
        ViperProfile.Aes256Gcm,
        ViperProfile.ProtectedBrotli,
        ViperProfile.Headerless,
    ];

    /// <summary>The representative profiles that write version 1 frames.</summary>
    internal static IReadOnlyList<ViperProfile> RepresentativeFramed { get; } =
        [.. Representative.Where(profile => profile != ViperProfile.Headerless)];

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
        ViperProfile.XxHash3 => "B-P4x3",
        ViperProfile.XxHash128 => "B-P4x128",
        ViperProfile.ChaCha20Poly1305 => "B-P5c",
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
            BinarySerializerOptions.Configure().WithCompression(new DeflateCompression()).Build(),

        ViperProfile.Brotli =>
            BinarySerializerOptions.Configure().WithCompression(new BrotliCompression()).Build(),

        ViperProfile.Crc32 =>
            BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum()).Build(),

        ViperProfile.Aes256Gcm =>
            BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), Key, "bench").Build(),

        ViperProfile.XxHash3 =>
            BinarySerializerOptions.Configure().WithChecksum(new XxHash3Checksum()).Build(),

        ViperProfile.XxHash128 =>
            BinarySerializerOptions.Configure().WithChecksum(new XxHash128Checksum()).Build(),

        ViperProfile.ChaCha20Poly1305 =>
            BinarySerializerOptions.Configure()
                .WithEncryption(new ChaCha20Poly1305Encryption(), Key, "bench")
                .Build(),

        ViperProfile.ProtectedBrotli =>
            BinarySerializerOptions.Configure()
                .WithCompression(new BrotliCompression())
                .WithChecksum(new Crc32Checksum())
                .WithEncryption(new Aes256GcmEncryption(), Key, "bench")
                .Build(),

        ViperProfile.ProtectedDeflate =>
            BinarySerializerOptions.Configure()
                .WithCompression(new DeflateCompression())
                .WithChecksum(new Crc32Checksum())
                .WithEncryption(new Aes256GcmEncryption(), Key, "bench")
                .Build(),

        ViperProfile.Headerless =>
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(),

        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
