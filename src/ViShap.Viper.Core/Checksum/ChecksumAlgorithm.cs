namespace ViShap.Viper.Checksum;

/// <summary>
/// Identifies the checksum stored with a payload. The value is recorded in the header, so a reader
/// can verify without being told which algorithm was used.
/// </summary>
/// <remarks>
/// A checksum detects accidental corruption. It is not a message authentication code and proves
/// nothing against a deliberate modification — an attacker can recompute it. For that, use
/// authenticated encryption, which also binds the header.
/// </remarks>
public enum ChecksumAlgorithm : byte
{
    /// <summary>No checksum is computed or verified.</summary>
    None = 0,

    /// <summary>CRC-32 (IEEE 802.3), 4 bytes. Fast, suitable for detecting accidental corruption.</summary>
    Crc32 = 1,

    /// <summary>XXH3, 64-bit, 8 bytes. Very fast on large payloads, with a far lower collision rate than CRC-32.</summary>
    XxHash3 = 2,

    /// <summary>XXH3, 128-bit, 16 bytes. For very large volumes of data, where 64 bits leave a collision within reach.</summary>
    XxHash128 = 3,

    /// <summary>
    /// A user-supplied algorithm, identified by name. Register it with
    /// <c>BinarySerializerOptions.Configure().RegisterCustomChecksum(...)</c> before reading a
    /// payload that names it.
    /// </summary>
    Custom = 255
}
