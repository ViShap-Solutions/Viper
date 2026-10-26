using System.Globalization;
using System.Text;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

internal sealed record FormatSizeRow(string Checkpoint, string Case, int Bytes);

/// <summary>
/// §14 — the sizes the format itself decides, read off the public serializer without a timing:
/// SIZE-09, the V1 header for each number of service records, as the frame minus the V0 payload of
/// the same value; SIZE-10, the null fold on a wide nullable record and on a list of strings.
/// </summary>
internal static class FormatSizeReport
{
    private static readonly byte[] Key = new byte[32];

    internal static IReadOnlyList<FormatSizeRow> Collect()
    {
        var rows = new List<FormatSizeRow>();
        var headerless = new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).Build());
        const string value = "a string of a fixed length";

        var headers = new (string Case, BinarySerializerOptionsBuilder Options)[]
        {
            ("no service", BinarySerializerOptions.Configure()),
            ("checksum (CRC-32)", BinarySerializerOptions.Configure().WithChecksum(new Crc32Checksum())),
            ("checksum + compression (Deflate)", BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32Checksum()).WithCompression(new DeflateCompression())),
            ("checksum + compression + encryption (AES-256-GCM, key id \"k7\")", BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32Checksum()).WithCompression(new DeflateCompression())
                .WithEncryption(new Aes256GcmEncryption(), Key, "k7"))
        };

        foreach (var (name, options) in headers)
        {
            byte[] frame = new BinarySerializer(options.Build()).Serialize(value);
            rows.Add(new FormatSizeRow("SIZE-09", name, frame.Length - OnDiskLength(frame)));
        }

        rows.Add(new FormatSizeRow("SIZE-10", "wide nullable record, 16 members, all null", headerless.Serialize(new WideNullable()).Length));
        rows.Add(new FormatSizeRow("SIZE-10", "wide nullable record, 16 members, all set", headerless.Serialize(WideNullable.Filled()).Length));
        rows.Add(new FormatSizeRow("SIZE-10", "list of 1 000 strings of 8 bytes", headerless.Serialize(Strings(1_000, 8)).Length));
        rows.Add(new FormatSizeRow("SIZE-10", "list of 1 000 null strings", headerless.Serialize(new List<string?>(new string?[1_000])).Length));

        return rows;
    }

    internal static void Write(IReadOnlyList<FormatSizeRow> rows, string path)
    {
        var csv = new StringBuilder("checkpoint,case,bytes\n");

        foreach (var row in rows)
            csv.Append(CultureInfo.InvariantCulture, $"{row.Checkpoint},\"{row.Case.Replace("\"", "\"\"")}\",{row.Bytes}\n");

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// The bytes after the header, as its last field declares them: the payload itself, or what
    /// compression and encryption made of it. The header is what remains of the frame.
    /// </summary>
    private static int OnDiskLength(byte[] frame)
    {
        int position = 4 + 1 + 1;
        int services = frame[position++];
        for (int record = 0; record < services; record++)
        {
            position += SevenBitLength(frame, position);
            int body = ReadSevenBit(frame, ref position);
            position += body;
        }

        return ReadSevenBit(frame, ref position);
    }

    private static int SevenBitLength(byte[] bytes, int position)
    {
        int length = 1;
        while ((bytes[position++] & 0x80) != 0)
            length++;

        return length;
    }

    private static int ReadSevenBit(byte[] bytes, ref int position)
    {
        int result = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte current = bytes[position++];
            result |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return result;
        }
    }

    private static List<string> Strings(int count, int length) =>
        [.. Enumerable.Range(0, count).Select(index => index.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0'))];

    /// <summary>A record whose sixteen members are all of types that can be null.</summary>
    public sealed class WideNullable
    {
        public string? A { get; set; }
        public string? B { get; set; }
        public string? C { get; set; }
        public string? D { get; set; }
        public int? E { get; set; }
        public int? F { get; set; }
        public int? G { get; set; }
        public int? H { get; set; }
        public List<int>? I { get; set; }
        public List<int>? J { get; set; }
        public List<int>? K { get; set; }
        public List<int>? L { get; set; }
        public byte[]? M { get; set; }
        public byte[]? N { get; set; }
        public Dictionary<int, int>? O { get; set; }
        public Dictionary<int, int>? P { get; set; }

        public static WideNullable Filled() => new()
        {
            A = "a", B = "b", C = "c", D = "d",
            E = 1, F = 2, G = 3, H = 4,
            I = [1], J = [2], K = [3], L = [4],
            M = [1], N = [2],
            O = new() { [1] = 1 }, P = new() { [2] = 2 }
        };
    }
}
