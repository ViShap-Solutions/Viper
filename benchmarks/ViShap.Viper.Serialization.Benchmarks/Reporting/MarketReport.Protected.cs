using System.Globalization;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// T4 of the Track B report: Viper's protected envelope against the composed baselines of §17, from
/// <c>protected-verification.csv</c>, <c>protected-rationale.csv</c> and the cells of
/// <c>MarketProtectedBenchmarks</c>. A run taken before T4 existed holds none of these files and gets no
/// section.
/// </summary>
internal static partial class MarketReport
{
    private const string ProtectedSuite = "MarketProtectedBenchmarks";

    private const string ProtectedVerification = "protected-verification.csv";

    /// <summary>One configuration of T4, as its verification names it.</summary>
    private sealed record Protection(string Library, string Version, string Name, bool Compressed);

    /// <summary>Everything T4's section and charts read, loaded once.</summary>
    private sealed record ProtectedRun(
        IReadOnlyList<Protection> Configurations,
        IReadOnlyList<Pair> Pairs,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Rationales,
        IReadOnlyList<Cell> Cells)
    {
        internal IReadOnlyList<string> Datasets { get; } = [.. Pairs.Select(pair => pair.Dataset).Distinct()];

        internal ResultRow? Find(string name, string dataset, string method) =>
            Cells.FirstOrDefault(cell => cell.Name == name && cell.Dataset == dataset && Title(cell.Workload).StartsWith(method, StringComparison.Ordinal))?.Row;
    }

    private static ProtectedRun? LoadProtected(string directory, IReadOnlyList<ResultRow> results)
    {
        var path = Path.Combine(directory, ProtectedVerification);

        if (!File.Exists(path))
        {
            return null;
        }

        var records = Csv.Read(path);
        string Name(IReadOnlyDictionary<string, string> record) => $"{record["library"]} {record["configuration"]}";

        var configurations = records
            .Select(record => new Protection(record["library"], record["version"], Name(record), bool.Parse(record["compressed"])))
            .DistinctBy(configuration => configuration.Name)
            .ToList();

        var known = configurations.Select(configuration => configuration.Name).ToHashSet(StringComparer.Ordinal);
        var cells = new List<Cell>();

        foreach (var row in results.Where(row => row.SuiteName == ProtectedSuite))
        {
            if (row.Parameter("Case") is { } @case && @case.Split(" · ") is [var name, var dataset] && known.Contains(name))
            {
                cells.Add(new Cell(name, dataset, Workload(row), row));
            }
        }

        var rationale = Path.Combine(directory, "protected-rationale.csv");

        return new ProtectedRun(
            configurations,
            [.. records.Select(record => new Pair(
                Name(record), record["dataset"], record["state"], long.Parse(record["buffered_bytes"], CultureInfo.InvariantCulture), record["detail"]))],
            File.Exists(rationale) ? Csv.Read(rationale) : [],
            cells);
    }

    private static void Protected(Document document, ProtectedRun? run)
    {
        if (run is null)
        {
            return;
        }

        document.Heading(2, "T4 — the protected envelope against composed baselines");
        document.Paragraph(
            "No library of the roster ships an authenticated envelope, so T4 has no library peer. It is compared " +
            "with what an application would otherwise write around its serializer (§17): the library's own " +
            "payload, CRC-32 over it, Brotli where the row says so, AES-256-GCM, and a hand-written header " +
            "carrying what the V1 header carries for the same configuration — the lengths, the checksum, the " +
            "key id, the nonce and the tag. Each part is the platform's own in its fastest documented form. " +
            $"Every pair below round-tripped, and a frame with one byte of its tag changed was refused, before " +
            $"a suite timed it ({run.Pairs.Count} pairs, `{ProtectedVerification}`).");

        document.Paragraph("What a composed baseline does **not** provide, which the Viper rows pay for (COMP-04):");
        document.Bullets(
        [
            "**Authenticated metadata.** Its header is not associated data: a changed length or flag is caught " +
            "only if the decryption or the checksum happens to fail. Every byte of a V1 header is bound to the tag.",
            "**Self-description and negotiation.** Its header names no algorithm, so a reader must be configured " +
            "exactly as the writer was. A V1 header names the algorithm of each phase, and the self-description " +
            "probe shows a reader configured with nothing undoing a V1 frame's compression and checksum.",
            "**Limits and budgets.** It allocates whatever its header declares — a forged uncompressed length " +
            "rents that many bytes — and bounds nothing the library itself does not. Viper checks every declared " +
            "length against its limits before allocating, under the shipping `SerializationLimits.Default`.",
            "**Key resolution.** It carries the key id's bytes and nothing resolves them; Viper reads them through " +
            "an `IKeyProvider`.",
        ]);

        document.Paragraph(
            "Each table compares configurations that do the same work: the Viper row and the composed baselines " +
            "that compress, then the Viper row and the ones that do not. A cell is the mean with its 99.9 % margin, " +
            "the ratio to the Viper row, `≈` where the intervals overlap, and the allocation per operation. Where " +
            "a composed baseline is faster, the list above is what the difference buys (COMP-05).");

        foreach (var compressed in new[] { true, false })
        {
            var group = run.Configurations.Where(configuration => configuration.Compressed == compressed).ToList();
            var reference = group.FirstOrDefault(configuration => configuration.Library == Viper);

            if (group.Count == 0)
            {
                continue;
            }

            document.Heading(3, compressed ? "T4 with compression — Brotli, CRC-32, AES-256-GCM" : "T4 without compression — CRC-32, AES-256-GCM");

            foreach (var dataset in run.Datasets)
            {
                document.Heading(4, $"T4 · {dataset}{(compressed ? " · Brotli" : string.Empty)}");
                document.Table(
                    ["Tier", "Configuration", "Frame bytes", "WL-01 serialize → byte[]", "WL-04 deserialize ← byte[]"],
                    [.. group.Select(configuration =>
                    {
                        var pair = run.Pairs.FirstOrDefault(entry => entry.Name == configuration.Name && entry.Dataset == dataset);

                        if (pair is not { State: "Supported" or "Partial" })
                        {
                            return (IReadOnlyList<string>)["T4", configuration.Name, pair?.State ?? "—", pair?.Detail ?? "—", "—"];
                        }

                        return (IReadOnlyList<string>)
                        [
                            "T4",
                            configuration.Name,
                            pair.Bytes.ToString("N0", CultureInfo.InvariantCulture),
                            ProtectedText(run, configuration, reference, dataset, "WL-01"),
                            ProtectedText(run, configuration, reference, dataset, "WL-04"),
                        ];
                    })]);
            }
        }

        if (run.Rationales.Count > 0)
        {
            document.Heading(3, "T4 configuration rationale");

            foreach (var record in run.Rationales)
            {
                document.Heading(4, $"{record["library"]} {record["version"]} — {record["configuration"]}");
                document.Bullets(
                [
                    $"**Mode used:** {record["mode"]}",
                    $"**Options set, and where they come from:** {record["options"]}",
                    $"**Deliberately not enabled:** {record["not_enabled"]}",
                    $"**A known faster mode not used:** {record["faster_mode_not_used"]}",
                ]);
            }
        }
    }

    private static string ProtectedText(ProtectedRun run, Protection configuration, Protection? reference, string dataset, string method)
    {
        if (run.Find(configuration.Name, dataset, method) is not { } row)
        {
            return "not measured in this run";
        }

        if (row.State != "Supported")
        {
            return row.State;
        }

        var text = $"{ReportWriter.Time(row.MeanNs)} ±{row.RelativeErrorPercent.ToString("F1", CultureInfo.InvariantCulture)}%";

        if (reference is not null && reference != configuration
            && run.Find(reference.Name, dataset, method) is { State: "Supported", MeanNs: > 0 } baseline)
        {
            text += $" ×{(row.MeanNs / baseline.MeanNs).ToString("F2", CultureInfo.InvariantCulture)}";

            if (Math.Abs(row.MeanNs - baseline.MeanNs) <= row.MarginNs + baseline.MarginNs)
            {
                text += " ≈";
            }
        }

        return $"{text} · {ReportWriter.Bytes(row.AllocatedBytes)}";
    }
}
