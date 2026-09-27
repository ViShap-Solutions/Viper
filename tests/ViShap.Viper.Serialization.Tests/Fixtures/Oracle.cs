using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ViShap.Viper.Crypto;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>One admissible output of a case: its SHA-256 and the output itself in hex.</summary>
internal sealed record OracleOutput(string Sha256, string Hex);

/// <summary>
/// One recorded case of the oracle: its key and every output it may produce. A case has one output,
/// unless the runtime decides part of what the writer sees — the enumeration order of a hash container
/// keyed by strings — and then it has one per order. A case whose input comes from the host has no
/// output at all, only the reason in <paramref name="HostDependence"/>.
/// </summary>
internal sealed record OracleEntry(string Key, IReadOnlyList<OracleOutput> Outputs, string? HostDependence = null);

/// <summary>
/// The byte oracle: one line per corpus case, holding the SHA-256 of what the writer produced and the
/// output in hex. The hash decides; the hex is there so a mismatch can show both outputs and the first
/// offset at which they part. A line <c>key | sha hex</c> right after the line of the same key records
/// a further admissible output of that case. A line <c>key ~ host: reason</c> records a case whose
/// input the host supplies — its culture, its time zone, names its operating system localizes — so the
/// case must be produced but its bytes are not compared.
/// </summary>
/// <remarks>
/// AES-256-GCM draws a fresh nonce for every message, so an encrypted frame never repeats. Such a frame
/// is compared as its header followed by the body decrypted in counter mode under the profile's key:
/// every byte the serializer decides is still covered, and only the nonce, and what it randomizes, is
/// left out.
/// </remarks>
internal static class Oracle
{
    private const int NonceLength = 12;

    private const int TagLength = 16;

    private const int BlockLength = 16;

    /// <summary>Loads the committed oracle from the output directory.</summary>
    public static IReadOnlyDictionary<string, OracleEntry> Load() =>
        Parse(File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Oracle", "oracle.txt")));

    /// <summary>
    /// Parses oracle lines; blank lines and lines starting with <c>#</c> are skipped. An alternative must
    /// follow the line of its own case and differ from every output already recorded for it.
    /// </summary>
    public static IReadOnlyDictionary<string, OracleEntry> Parse(IEnumerable<string> lines)
    {
        var outputs = new Dictionary<string, List<OracleOutput>>(StringComparer.Ordinal);
        var hostDependent = new Dictionary<string, string>(StringComparer.Ordinal);
        string? previous = null;

        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] parts = line.Split(' ');

            if (parts is [var hosted, "~", "host:", ..])
            {
                if (outputs.ContainsKey(hosted) || !hostDependent.TryAdd(hosted, string.Join(' ', parts[3..])))
                    throw new InvalidOperationException($"The oracle records {hosted} twice.");

                previous = null;
                continue;
            }

            if (parts is [var key, "|", var sha, var hex])
            {
                if (key != previous)
                    throw new InvalidOperationException($"An alternative of {key} does not follow the line of its case.");

                if (outputs[key].Any(output => output.Sha256 == sha))
                    throw new InvalidOperationException($"The oracle records the same output of {key} twice.");

                outputs[key].Add(new OracleOutput(sha, hex));
                continue;
            }

            if (parts is not [var name, var hash, var bytes])
                throw new InvalidOperationException($"An oracle line has {parts.Length} fields instead of 3: {line}");

            if (hostDependent.ContainsKey(name) || !outputs.TryAdd(name, [new OracleOutput(hash, bytes)]))
                throw new InvalidOperationException($"The oracle records {name} twice.");

            previous = name;
        }

        var entries = outputs.ToDictionary(
            pair => pair.Key, pair => new OracleEntry(pair.Key, pair.Value), StringComparer.Ordinal);

        foreach (var (key, reason) in hostDependent)
            entries.Add(key, new OracleEntry(key, [], reason));

        return entries;
    }

    /// <summary>The oracle line for a case whose input the host supplies, with the reason.</summary>
    public static string HostDependent(string key, string reason) => $"{key} ~ host: {reason}";

    /// <summary>The oracle line for one case.</summary>
    public static string Line(string key, byte[] output) => $"{key} {Sha256(output)} {Convert.ToHexStringLower(output)}";

    /// <summary>The oracle line for a further admissible output of the case on the line before it.</summary>
    public static string Alternative(string key, byte[] output) =>
        $"{key} | {Sha256(output)} {Convert.ToHexStringLower(output)}";

    /// <summary>The lower-case hex SHA-256 of <paramref name="output"/>.</summary>
    public static string Sha256(byte[] output) => Convert.ToHexStringLower(SHA256.HashData(output));

    /// <summary>
    /// Compares what the writer produced with the oracle and returns every difference, or an empty string
    /// when there is none: a case the oracle records that was not produced, a case produced that the
    /// oracle does not record, and every output whose hash differs, with both outputs in hex.
    /// </summary>
    public static string Compare(
        IReadOnlyDictionary<string, OracleEntry> expected, IReadOnlyList<(string Key, byte[] Output)> actual)
    {
        var report = new StringBuilder();
        var produced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, output) in actual)
        {
            if (!produced.Add(key))
            {
                report.AppendLine($"{key}: produced twice");
                continue;
            }

            if (!expected.TryGetValue(key, out var entry))
            {
                report.AppendLine($"{key}: not in the oracle");
                report.AppendLine($"  actual   {Convert.ToHexStringLower(output)}");
                continue;
            }

            if (entry.HostDependence is not null)
                continue;

            string hash = Sha256(output);

            if (entry.Outputs.Any(recorded => recorded.Sha256 == hash))
                continue;

            report.AppendLine($"{key}: {FirstDifference(Convert.FromHexString(entry.Outputs[0].Hex), output)}");

            foreach (var recorded in entry.Outputs)
                report.AppendLine($"  expected {recorded.Hex}");

            report.AppendLine($"  actual   {Convert.ToHexStringLower(output)}");
        }

        foreach (var key in expected.Keys.Where(key => !produced.Contains(key)).Order(StringComparer.Ordinal))
            report.AppendLine($"{key}: recorded in the oracle but not produced");

        return report.ToString();
    }

    /// <summary>
    /// The output as the oracle compares it. A frame encrypted with AES-256-GCM becomes its header followed
    /// by the plaintext of its body; anything else is returned unchanged.
    /// </summary>
    public static byte[] Normalize(byte[] output, byte[]? key)
    {
        if (key is null)
            return output;

        var header = Wire.ReadHeader(output);

        if (header.Encryption != (byte)EncryptionAlgorithm.Aes256Gcm)
            return output;

        var body = output.AsSpan(header.HeaderLength);
        var nonce = body[..NonceLength];
        var ciphertext = body[NonceLength..^TagLength];

        byte[] normalized = new byte[header.HeaderLength + ciphertext.Length];
        output.AsSpan(0, header.HeaderLength).CopyTo(normalized);
        DecryptCounterMode(key, nonce, ciphertext, normalized.AsSpan(header.HeaderLength));

        return normalized;
    }

    private static string FirstDifference(byte[] expected, byte[] actual)
    {
        int common = Math.Min(expected.Length, actual.Length);

        for (int offset = 0; offset < common; offset++)
        {
            if (expected[offset] != actual[offset])
                return $"first difference at offset {offset} (expected {expected.Length} bytes, actual {actual.Length})";
        }

        return $"identical for {common} bytes, then expected {expected.Length} bytes and actual {actual.Length}";
    }

    /// <summary>
    /// GCM encrypts with AES in counter mode: block <c>i</c> of the body is XORed with
    /// <c>AES(key, nonce || i + 2)</c>, the counter 1 being reserved for the tag. The tag is not checked.
    /// </summary>
    private static void DecryptCounterMode(
        byte[] key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
    {
        int blocks = (ciphertext.Length + BlockLength - 1) / BlockLength;
        byte[] counters = new byte[blocks * BlockLength];

        for (int block = 0; block < blocks; block++)
        {
            var counter = counters.AsSpan(block * BlockLength, BlockLength);
            nonce.CopyTo(counter);
            BinaryPrimitives.WriteUInt32BigEndian(counter[NonceLength..], (uint)(block + 2));
        }

        using var aes = Aes.Create();
        aes.Key = key;
        byte[] keystream = aes.EncryptEcb(counters, PaddingMode.None);

        for (int index = 0; index < ciphertext.Length; index++)
            plaintext[index] = (byte)(ciphertext[index] ^ keystream[index]);
    }
}
