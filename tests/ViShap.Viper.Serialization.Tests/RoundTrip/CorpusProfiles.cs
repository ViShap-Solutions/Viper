using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Profile P0: the corpus under the default configuration — V1, no compression, no checksum, no
/// encryption. This is the baseline every §19 item is measured against.
/// </summary>
public sealed class DefaultCorpusTests : Corpus
{
    protected override BinarySerializer Serializer { get; } = new();

    protected override BinarySerializer WithLimits(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());
}

/// <summary>
/// RT-C08, profile P7: the same corpus through the headerless format. V0 supports every shape in the
/// corpus — it lacks only the envelope's four phases and reference framing, none of which the corpus
/// uses — so anything that round-trips under V1 must round-trip here unchanged (§10.2, §22.8).
/// </summary>
public sealed class HeaderlessCorpusTests : Corpus
{
    protected override BinarySerializer Serializer { get; } = new(
        BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    protected override BinarySerializer WithLimits(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure()
            .WithVersion(0)
            .AllowV0Fallback()
            .WithLimits(limits)
            .Build());
}

/// <summary>
/// RT-C09, profile P6: the same corpus through the full V1 envelope — Brotli, CRC-32 and AES-256-GCM
/// over a fixed key. The phases sit outside the payload, so no family may encode differently for
/// being compressed, checksummed and encrypted (§22.6).
/// </summary>
public sealed class ProtectedCorpusTests : Corpus
{
    private static readonly byte[] Key =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    protected override BinarySerializer Serializer { get; } = new(Build(SerializationLimits.Default));

    protected override BinarySerializer WithLimits(SerializationLimits limits) => new(Build(limits));

    private static BinarySerializerOptions Build(SerializationLimits limits) =>
        BinarySerializerOptions.Configure()
            .WithCompression(new Brotli())
            .WithChecksum(new Crc32())
            .WithEncryption(new Aes256Gcm(), Key)
            .WithLimits(limits)
            .Build();
}
