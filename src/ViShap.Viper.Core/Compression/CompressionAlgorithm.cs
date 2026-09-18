namespace ViShap.Viper.Compression;

/// <summary>
/// Identifies the compression applied to a payload. The value is recorded in the header, so a reader
/// can pick the right algorithm without being told.
/// </summary>
public enum CompressionAlgorithm : byte
{
    /// <summary>No compression; the payload is stored as produced.</summary>
    None = 0,

    /// <summary>Raw DEFLATE (RFC 1951). Broad compatibility, moderate ratio.</summary>
    Deflate = 1,

    /// <summary>Brotli (RFC 7932). Better ratio than DEFLATE, usually at a higher CPU cost.</summary>
    Brotli = 2,

    /// <summary>
    /// A user-supplied algorithm, identified by name. Register it with
    /// <c>BinarySerializerOptions.Configure().RegisterCustomCompression(...)</c> before reading a
    /// payload that names it.
    /// </summary>
    Custom = 255
}
