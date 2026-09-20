using System.Text;

namespace ViShap.Viper.Diagnostics;

/// <summary>
/// Diagnostic rendering of a payload's envelope. This is tooling, not production behavior: it turns
/// a failure into readable output, so unlike the serializer it is allowed to report an error as text
/// instead of propagating it.
/// </summary>
public static class BinaryFormatDumper
{
    /// <summary>Renders the envelope of a payload as human-readable text.</summary>
    /// <param name="payload">The payload bytes.</param>
    /// <returns>
    /// A short report naming the format version and the algorithms, or a description of why the header
    /// could not be read. This method does not throw for malformed input.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    public static string DumpHeader(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var stream = new MemoryStream(payload, writable: false);
        return DumpHeader(stream);
    }

    /// <summary>Renders the envelope of a payload as human-readable text.</summary>
    /// <param name="source">
    /// A seekable stream positioned at the start of a payload. Its position is restored.
    /// </param>
    /// <returns>
    /// A short report naming the format version and the algorithms, or a description of why the header
    /// could not be read. This method does not throw for malformed input.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="source"/> cannot seek. Reading a header without consuming the stream needs
    /// seekability, so this is a caller mistake rather than something to report as text.
    /// </exception>
    public static string DumpHeader(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var report = new StringBuilder();

        try
        {
            var info = BinaryFormatInspector.Peek(source);
            if (info is null)
            {
                report.AppendLine("No recognized Viper header (V0 payload or unrelated data).");
                return report.ToString();
            }

            var header = info.Value;
            report.AppendLine($"Format version : {header.FormatVersion}");
            report.AppendLine($"Compression    : {Describe(header.Compression, header.CustomCompressionName)}");
            report.AppendLine($"Checksum       : {Describe(header.ChecksumAlgorithm, header.CustomChecksumName)}");
            report.AppendLine($"Encryption     : {Describe(header.Encryption, header.CustomEncryptionName)}");
            report.AppendLine($"Key id         : {header.KeyId ?? "(none)"}");
        }
        catch (BinarySerializerException ex)
        {
            report.AppendLine($"Header could not be read: {ex.GetType().Name}: {ex.Message}");
        }

        return report.ToString();
    }

    private static string Describe<TKind>(TKind kind, string? customName) where TKind : Enum =>
        customName is null ? kind.ToString() : $"{kind} ('{customName}')";
}
