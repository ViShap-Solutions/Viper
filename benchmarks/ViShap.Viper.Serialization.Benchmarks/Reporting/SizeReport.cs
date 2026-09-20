using System.Globalization;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

internal sealed record SizeRow(
    string Profile,
    string Dataset,
    string State,
    int Bytes,
    int EnvelopeBytes,
    int ReferenceFramingBytes,
    double CompressionRatio);

/// <summary>
/// §14 — payload size, recorded without a timing in the same table and from the same serialization
/// that verified (FAIR-27). The derived columns are differences between profiles on one value:
/// the envelope against V0, reference framing against the default, compression against no compression.
/// </summary>
internal static class SizeReport
{
    internal static IReadOnlyList<SizeRow> Collect()
    {
        var sizes = new Dictionary<(ViperProfile Profile, string Dataset), (int Bytes, string State)>();

        foreach (var profile in ViperProfiles.All)
        {
            var adapter = new ViperAdapter(profile);
            var references = profile == ViperProfile.PreserveReferences;

            foreach (var dataset in Corpus.All)
            {
                if (dataset.RequiresReferences && !references)
                {
                    sizes[(profile, dataset.Id)] = (0, nameof(VerificationState.Unsupported));
                    continue;
                }

                var result = RoundTripVerifier.Verify(adapter, adapter, dataset, references);
                sizes[(profile, dataset.Id)] = (result.BufferedBytes, result.State.ToString());
            }
        }

        var rows = new List<SizeRow>();

        foreach (var ((profile, dataset), (bytes, state)) in sizes)
        {
            var headerless = Lookup(sizes, ViperProfile.Headerless, dataset);
            var plain = Lookup(sizes, ViperProfile.Default, dataset);

            rows.Add(new SizeRow(
                ViperProfiles.PlanId(profile),
                dataset,
                state,
                bytes,
                Envelope(profile, bytes, headerless),
                profile == ViperProfile.PreserveReferences && plain > 0 ? bytes - plain : 0,
                Compressed(profile) && plain > 0 ? (double)bytes / plain : 1.0));
        }

        return rows;
    }

    internal static void Write(IReadOnlyList<SizeRow> rows, string path)
    {
        var csv = new StringBuilder("profile,dataset,state,bytes,envelope_bytes,reference_framing_bytes,compression_ratio\n");

        foreach (var row in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{row.Profile},{row.Dataset},{row.State},{row.Bytes},{row.EnvelopeBytes},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{row.ReferenceFramingBytes},{row.CompressionRatio.ToString("F4", CultureInfo.InvariantCulture)}\n");
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Reads the table back, so the report and the charts are a view over the committed file rather
    /// than over the process that measured it.
    /// </summary>
    internal static IReadOnlyList<SizeRow> Read(string path)
    {
        var rows = new List<SizeRow>();

        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = line.Split(',');

            rows.Add(new SizeRow(
                cells[0],
                cells[1],
                cells[2],
                int.Parse(cells[3], CultureInfo.InvariantCulture),
                int.Parse(cells[4], CultureInfo.InvariantCulture),
                int.Parse(cells[5], CultureInfo.InvariantCulture),
                double.Parse(cells[6], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static int Lookup(
        Dictionary<(ViperProfile, string), (int Bytes, string State)> sizes,
        ViperProfile profile,
        string dataset) =>
        sizes.TryGetValue((profile, dataset), out var entry) ? entry.Bytes : 0;

    /// <summary>What the V1 envelope adds over the headerless payload of the same value (§22.8).</summary>
    private static int Envelope(ViperProfile profile, int bytes, int headerless) =>
        profile == ViperProfile.Default && headerless > 0 ? bytes - headerless : 0;

    private static bool Compressed(ViperProfile profile) => profile
        is ViperProfile.Deflate
        or ViperProfile.Brotli
        or ViperProfile.ProtectedBrotli
        or ViperProfile.ProtectedDeflate;
}
