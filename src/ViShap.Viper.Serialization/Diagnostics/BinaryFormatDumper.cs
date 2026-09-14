using System.Text;

namespace ViShap.Viper.Diagnostics;

public readonly record struct TraceEntry(long Offset, int Depth, string TypeName, string? Value);

public static class BinaryFormatDumper
{
    public static string DumpHeader(Stream source)
    {
        var info = BinaryFormatInspector.Peek(source);
        if (info is null) return "Not a recognized BinarySerializer stream (magic number mismatch).";

        var i = info.Value;
        return $"""
            FormatVersion    : {i.FormatVersion}
            Compression      : {i.Compression}{(i.CustomCompressionName is { } c ? $" (custom: {c})" : "")}
            ChecksumAlgorithm: {i.ChecksumAlgorithm}{(i.CustomChecksumName is { } cs ? $" (custom: {cs})" : "")}
            Encryption       : {i.Encryption}{(i.CustomEncryptionName is { } e ? $" (custom: {e})" : "")}
            KeyId            : {i.KeyId ?? "(none)"}
            """;
    }
    
    public static string Dump<T>(byte[] bytes, BinarySerializerOptions? options = null)
    {
        var header = DumpHeader(new MemoryStream(bytes));
        var sb = new StringBuilder();
        sb.AppendLine("=== Header ===");
        sb.AppendLine(header);
        sb.AppendLine();

        try
        {
            var serializer = new BinarySerializer(options ?? BinarySerializerOptions.Default);
            var result = serializer.Deserialize<T>(bytes);
            sb.AppendLine("=== Parsed successfully ===");
            sb.AppendLine(ObjectGraphDumper.Dump(result));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"=== Parsing FAILED: {ex.GetType().Name}: {ex.Message} ===");
            sb.AppendLine();
            sb.AppendLine("=== Last reads before failure ===");

            foreach (var entry in TraceFailedRead<T>(bytes, options))
            {
                string valueText = entry.Value is null ? "" : $": {entry.Value}";
                sb.AppendLine($"[Offset 0x{entry.Offset:X}] (depth {entry.Depth}) {entry.TypeName}{valueText}");
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<TraceEntry> TraceFailedRead<T>(byte[] bytes, BinarySerializerOptions? options)
    {
        BinaryPayloadReader? payloadReader = null;

        try
        {
            var opts = options ?? BinarySerializerOptions.Default;

            using var ms = new MemoryStream(bytes);

            using var reader = new BinaryReader(ms);

            payloadReader =
                new BinaryPayloadReader(
                    reader,
                    opts.PreserveReferences,
                    opts.Limits,
                    enableTrace: true);

            payloadReader.Deserialize<T>();

            return payloadReader.Trace;
        }
        catch (Exception)
        {
            return payloadReader?.Trace ?? Array.Empty<TraceEntry>();
        }
    }
}