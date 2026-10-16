using System.Globalization;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// The sections of a Track B report, drawn from the run's own files. It opens with the tiers, the roster
/// and what was not measured before any number (REP-05, RST-06), prints every configuration's rationale
/// (FAIR-13) and every structural model difference beside the datasets it touches (FAIR-20), and shows a
/// timing only in a table that names its tier and holds only the configurations the probes placed there
/// (TIER-01, TIER-02). A compressed configuration appears in one section, against Viper's compressed
/// profile and nothing else (FAIR-16).
/// </summary>
/// <remarks>
/// Rows keep the roster's order and are never sorted by a number. A ratio is to Viper's configuration of
/// the same table, beside the absolute it comes from; <c>≈</c> marks two cells whose 99.9 % intervals
/// overlap, which is no measurable difference and not a ranking.
/// </remarks>
internal static class MarketReport
{
    internal const string Track = "B — the market";

    private const string Viper = "ViShap.Viper";

    private const string Evolution = "MarketEvolutionBenchmarks";

    /// <summary>The z of a two-sided 99.9 % interval, applied to a cell's standard error.</summary>
    private const double Z999 = 3.29;

    private static readonly string[] Tiers = ["T0", "T1", "T2", "T3"];

    private sealed record Configuration(
        string Library, string Version, string Name, string Family, string Intended, bool Compressed, bool BufferWriter, bool Span);

    private sealed record Placement(string Tier, string Name, bool Intended, bool Qualified, string Reason);

    private sealed record Pair(string Name, string Dataset, string State, long Bytes, string Detail);

    private sealed record Cell(string Name, string Dataset, string Workload, ResultRow Row);

    /// <summary>Everything the sections read, loaded once.</summary>
    private sealed class Run
    {
        internal required IReadOnlyList<Configuration> Configurations { get; init; }

        internal required IReadOnlyList<IReadOnlyDictionary<string, string>> Unmeasured { get; init; }

        internal required IReadOnlyList<Placement> Placements { get; init; }

        internal required IReadOnlyList<Pair> Pairs { get; init; }

        internal required IReadOnlyList<IReadOnlyDictionary<string, string>> Capabilities { get; init; }

        internal required IReadOnlyList<IReadOnlyDictionary<string, string>> Rationales { get; init; }

        internal required IReadOnlyList<IReadOnlyDictionary<string, string>> Differences { get; init; }

        internal required IReadOnlyList<(string Id, string Name, string Tiers, bool Core, string[] Types)> Datasets { get; init; }

        internal required IReadOnlyList<Cell> Cells { get; init; }

        /// <summary>The workloads in the order the suites measured them, the evolution read last.</summary>
        internal required IReadOnlyList<string> Workloads { get; init; }

        internal Configuration? Find(string name) => Configurations.FirstOrDefault(configuration => configuration.Name == name);

        internal IEnumerable<Configuration> Members(string tier) =>
            Configurations.Where(configuration => Placements.Any(placement =>
                placement.Tier == tier && placement.Name == configuration.Name && placement is { Intended: true, Qualified: true }));

        internal Pair? PairOf(string name, string dataset) =>
            Pairs.FirstOrDefault(pair => pair.Name == name && pair.Dataset == dataset);
    }

    internal static void Write(Document document, string directory, IReadOnlyList<ResultRow> results)
    {
        var run = Load(directory, results);

        Caveats(document);
        TierTable(document, run);
        Roster(document, run);
        ReportWriter.Environment(document, directory);
        Verification(document, run);
        Probes(document, run);
        Rationale(document, run);
        Differences(document, run);
        Sizes(document, run);
        Comparisons(document, run);
        Compressed(document, run);
        ReportWriter.Caution(document, results);
    }

    private static Run Load(string directory, IReadOnlyList<ResultRow> results)
    {
        string Name(IReadOnlyDictionary<string, string> record) => $"{record["library"]} {record["configuration"]}";

        var roster = Csv.Read(Path.Combine(directory, MarketArtifacts.Roster));

        var configurations = roster
            .Where(record => record["kind"] == "measured")
            .Select(record => new Configuration(
                record["library"], record["version"], Name(record), record["family"], record["intended"],
                bool.Parse(record["compressed"]), bool.Parse(record["buffer_writer"]), bool.Parse(record["span"])))
            .ToList();

        var known = configurations.Select(configuration => configuration.Name).ToHashSet(StringComparer.Ordinal);
        var cells = new List<Cell>();
        var workloads = new List<string>();

        foreach (var row in results.Where(row => row.SuiteName.StartsWith("Market", StringComparison.Ordinal)))
        {
            if (row.Parameter("Case") is not { } @case || @case.Split(" · ") is not [var name, var dataset] || !known.Contains(name))
            {
                continue;
            }

            var workload = Workload(row);
            cells.Add(new Cell(name, dataset, workload, row));

            if (!workloads.Contains(workload))
            {
                workloads.Add(workload);
            }
        }

        return new Run
        {
            Configurations = configurations,
            Unmeasured = [.. roster.Where(record => record["kind"] != "measured")],
            Placements =
            [
                .. Csv.Read(Path.Combine(directory, "tiers.csv")).Select(record => new Placement(
                    record["tier"], Name(record), bool.Parse(record["intended"]), bool.Parse(record["qualified"]), record["reason"])),
            ],
            Pairs =
            [
                .. Csv.Read(Path.Combine(directory, "market-verification.csv")).Select(record => new Pair(
                    Name(record), record["dataset"], record["state"],
                    long.Parse(record["buffered_bytes"], CultureInfo.InvariantCulture), record["detail"])),
            ],
            Capabilities = Csv.Read(Path.Combine(directory, "capabilities.csv")),
            Rationales = Csv.Read(Path.Combine(directory, "rationale.csv")),
            Differences = Csv.Read(Path.Combine(directory, "model-differences.csv")),
            Datasets =
            [
                .. Csv.Read(Path.Combine(directory, MarketArtifacts.Datasets)).Select(record => (
                    record["dataset"], record["name"], record["tiers"], bool.Parse(record["core"]),
                    record["types"].Split(' ', StringSplitOptions.RemoveEmptyEntries))),
            ],
            Cells = cells,
            Workloads = [.. workloads.OrderBy(workload => workload.StartsWith(Evolution, StringComparison.Ordinal))],
        };
    }

    /// <summary>A cell's workload: the suite and the method title, so two suites' WL-22 stay apart.</summary>
    private static string Workload(ResultRow row) => $"{row.SuiteName}|{row.Method.Trim('\'')}";

    private static string Title(string workload) => workload[(workload.IndexOf('|', StringComparison.Ordinal) + 1)..];

    private static void Caveats(Document document)
    {
        document.Heading(2, "What this record is, and what it is not");

        document.Bullets(
        [
            "**Comparisons stay inside a tier.** A serializer that carries less is not faster at the same " +
            "job, so every table below names its tier and holds only the configurations whose probes placed " +
            "them in it. A configuration a probe left out is named under the table with the probe's reason.",

            "**There is no winner.** Rows keep the roster's order and are never sorted by a number. A ratio is " +
            "to Viper's configuration of the same table and stands beside the absolute it comes from; `≈` marks " +
            "two cells whose 99.9 % intervals overlap, which is no measurable difference.",

            "**Every competitor runs in its own documented best production mode**, as its rationale below says. " +
            "An adapter that could be configured better is a defect of this harness, and a report of one is " +
            "welcome.",

            "**A compressed configuration is compared only with Viper's compressed profile**, in its own section.",

            "**Payload sizes are properties of the formats** and hold on any machine; **every timing and every " +
            "allocation figure belongs to the machine below** and is not comparable with a figure taken elsewhere.",
        ]);
    }

    private static void TierTable(Document document, Run run)
    {
        document.Heading(2, "Capability tiers");
        document.Paragraph(
            "Placed by the probes of this run (`tiers.csv`, `capabilities.csv`), never by hand. A member is a " +
            "configuration meant for the tier whose probes qualify it; one that is meant for it and does not " +
            "qualify is left out of that tier's tables, with the probe's reason.");

        var rows = new List<IReadOnlyList<string>>();

        foreach (var tier in Tiers)
        {
            var placed = run.Placements.Where(placement => placement.Tier == tier).ToList();
            var members = placed.Where(placement => placement is { Intended: true, Qualified: true }).Select(placement => placement.Name);
            var leftOut = placed.Where(placement => placement is { Intended: true, Qualified: false })
                .Select(placement => $"{placement.Name}: {placement.Reason}");
            var alsoQualify = placed.Where(placement => placement is { Intended: false, Qualified: true }).Select(placement => placement.Name);

            rows.Add(
            [
                tier,
                string.Join(", ", members),
                string.Join("; ", leftOut) is { Length: > 0 } text ? text : "—",
                string.Join(", ", alsoQualify) is { Length: > 0 } also ? also : "—",
            ]);
        }

        rows.Add(
        [
            "T4",
            "—",
            "No library peer. The protected envelope is measured in Track A and against the composed baselines of §17",
            "—",
        ]);

        document.Table(["Tier", "Members", "Meant for it, left out by a probe", "Qualifies, not measured in it"], rows);
    }

    private static void Roster(Document document, Run run)
    {
        document.Heading(2, "Roster");
        document.Paragraph("The libraries measured, in the versions the run loaded, and the configurations each one runs in.");

        document.Table(
            ["Library", "Version", "Configuration", "Meant for", "Compressed"],
            [
                .. run.Configurations.Select(configuration => (IReadOnlyList<string>)
                [
                    configuration.Library,
                    configuration.Version,
                    configuration.Name[(configuration.Library.Length + 1)..],
                    configuration.Intended,
                    configuration.Compressed ? "yes" : "no",
                ]),
            ]);

        foreach (var (kind, heading, note) in new[]
                 {
                     ("second-circle", "Second circle — not measured in this run",
                      "Measured where they add information, never gating (§5.3)."),
                     ("excluded", "Excluded, with the reason",
                      "An exclusion names the criterion it fails. No exclusion is a claim that a library is bad, and none is justified by a result (§5.4)."),
                 })
        {
            var records = run.Unmeasured.Where(record => record["kind"] == kind).ToList();

            if (records.Count == 0)
            {
                continue;
            }

            document.Heading(3, heading);
            document.Paragraph(note);
            document.Table(
                ["Library", "Version", "Reason"],
                [.. records.Select(record => (IReadOnlyList<string>)[record["library"], record["version"] is { Length: > 0 } v ? v : "—", record["reason"]])]);
        }
    }

    private static void Verification(Document document, Run run)
    {
        document.Heading(2, "Verification before timing");
        document.Paragraph(
            $"{run.Pairs.Count} (configuration, dataset) pairs of the configurations' tiers were written, read " +
            "back and compared with the schema's value before any suite timed them; a suite times only a pair " +
            "found `Supported` or `Partial`, and round-trips it once more in its own setup.");

        document.Table(
            ["Configuration", "Supported", "Partial", "Unsupported", "Failed"],
            [
                .. run.Configurations.Select(configuration =>
                {
                    var pairs = run.Pairs.Where(pair => pair.Name == configuration.Name).ToList();
                    string Count(string state) => pairs.Count(pair => pair.State == state).ToString(CultureInfo.InvariantCulture);

                    return (IReadOnlyList<string>)[configuration.Name, Count("Supported"), Count("Partial"), Count("Unsupported"), Count("Failed")];
                }),
            ]);

        var explained = run.Pairs.Where(pair => pair.State != "Supported").ToList();

        if (explained.Count > 0)
        {
            document.Heading(3, "Pairs that are not Supported, and why");
            document.Table(
                ["Configuration", "Dataset", "State", "What the library does instead"],
                [.. explained.Select(pair => (IReadOnlyList<string>)[pair.Name, pair.Dataset, pair.State, pair.Detail])]);
        }
    }

    private static void Probes(Document document, Run run)
    {
        if (run.Capabilities.Count == 0)
        {
            return;
        }

        var capabilities = run.Capabilities.Select(record => record["capability"]).Distinct().ToList();

        document.Heading(2, "Capability probes");
        document.Paragraph("Each probe runs against the library's public API in the configuration it is measured in (§7.5).");

        document.Table(
            ["Configuration", .. capabilities],
            [
                .. run.Configurations.Select(configuration => (IReadOnlyList<string>)
                [
                    configuration.Name,
                    .. capabilities.Select(capability => run.Capabilities.FirstOrDefault(record =>
                            $"{record["library"]} {record["configuration"]}" == configuration.Name && record["capability"] == capability)
                        is { } found ? found["state"] : "—"),
                ]),
            ]);

        var details = run.Capabilities.Where(record => record["state"] != "Supported").ToList();

        if (details.Count > 0)
        {
            document.Heading(3, "What a probe that is not Supported observed");
            document.Table(
                ["Configuration", "Capability", "State", "Observed"],
                [.. details.Select(record => (IReadOnlyList<string>)
                    [$"{record["library"]} {record["configuration"]}", record["capability"], record["state"], record["detail"]])]);
        }
    }

    private static void Rationale(Document document, Run run)
    {
        document.Heading(2, "Configuration rationale");
        document.Paragraph(
            "Verbatim, as each adapter states it (FAIR-13), and in `configuration.md`. If you know a better " +
            "production configuration for a library, the adapter is wrong and should change.");

        foreach (var record in run.Rationales)
        {
            document.Heading(3, $"{record["library"]} {record["version"]} — {record["configuration"]}");
            document.Bullets(
            [
                $"**Mode used:** {record["mode"]}",
                $"**Options set, and where they come from:** {record["options"]}",
                $"**Deliberately not enabled:** {record["not_enabled"]}",
                $"**A known faster mode not used:** {record["faster_mode_not_used"]}",
            ]);
        }
    }

    private static void Differences(Document document, Run run)
    {
        document.Heading(2, "Structural model differences");

        if (run.Differences.Count == 0)
        {
            document.Paragraph("None: every library's models carry the schema's shape member for member.");
            return;
        }

        document.Paragraph(
            "Where a library cannot express the schema's shape, its model differs, and the difference is recorded " +
            "per member (FAIR-20). Every one is repeated under the table of each dataset it touches.");

        document.Table(
            ["Library", "Type", "Member", "Difference", "Reason", "Datasets"],
            [.. run.Differences.Select(record => (IReadOnlyList<string>)
            [
                Libraries(run, record["family"]),
                record["type"],
                record["member"],
                record["difference"],
                record["reason"],
                string.Join(", ", run.Datasets.Where(dataset => dataset.Types.Contains(record["type"])).Select(dataset => dataset.Id)) is { Length: > 0 } ids ? ids : "—",
            ])]);
    }

    private static string Libraries(Run run, string family) =>
        string.Join(", ", run.Configurations.Where(configuration => configuration.Family == family).Select(configuration => configuration.Library).Distinct()) is { Length: > 0 } names
            ? names
            : family;

    private static void Sizes(Document document, Run run)
    {
        document.Heading(2, "Payload size by tier");
        document.Paragraph("Bytes of the buffered payload of each verified pair. No timing appears in these tables.");

        foreach (var tier in Tiers)
        {
            var members = run.Members(tier).Where(configuration => !configuration.Compressed).ToList();
            var datasets = DatasetsOf(run, tier);

            if (members.Count == 0 || datasets.Count == 0)
            {
                continue;
            }

            document.Heading(3, $"{tier} — payload size");
            document.Table(
                ["Dataset", .. members.Select(configuration => configuration.Name)],
                [.. datasets.Select(dataset => (IReadOnlyList<string>)
                [
                    dataset.Id,
                    .. members.Select(configuration => run.PairOf(configuration.Name, dataset.Id) switch
                    {
                        { State: "Supported" or "Partial" } pair => pair.Bytes.ToString("N0", CultureInfo.InvariantCulture) + (pair.State == "Partial" ? " (Partial)" : string.Empty),
                        { } pair => pair.State,
                        null => "—",
                    }),
                ])]);
        }
    }

    private static void Comparisons(Document document, Run run)
    {
        if (run.Cells.Count == 0)
        {
            document.Heading(2, "Comparative results");
            document.Paragraph("No comparative suite produced a cell in this run.");
            return;
        }

        document.Heading(2, "Comparative results by tier");
        document.Paragraph(
            "Each cell is the mean with its relative margin of error, the ratio to Viper's configuration of the " +
            "same table, and the allocation per operation. A table names its tier in its heading and in its " +
            "first column, and holds only the tier's members.");

        foreach (var tier in Tiers)
        {
            var members = run.Members(tier).Where(configuration => !configuration.Compressed).ToList();

            if (members.Count == 0)
            {
                continue;
            }

            document.Heading(3, $"Tier {tier}");

            foreach (var dataset in DatasetsOf(run, tier))
            {
                Table(document, run, tier, members, dataset.Id, $"{tier} · {dataset.Id} {dataset.Name}",
                    workload => !workload.StartsWith(Evolution, StringComparison.Ordinal));
            }

            if (tier == "T2")
            {
                Table(document, run, tier, members, "DATA-13/evolution", "T2 · DATA-13 read by the evolved model (WL-15)",
                    workload => workload.StartsWith(Evolution, StringComparison.Ordinal));
            }
        }
    }

    private static void Compressed(Document document, Run run)
    {
        var compressed = run.Configurations.Where(configuration => configuration.Compressed).ToList();

        if (compressed.Count == 0)
        {
            return;
        }

        document.Heading(2, "Compressed configurations");
        document.Paragraph(
            "A library's built-in compression, measured on and off: off in the tier tables above, on here, against " +
            "Viper's compressed profile and nothing else (FAIR-16). The configurations sit in different tiers, so " +
            "the tier of each row is in the table.");

        var datasets = run.Datasets
            .Where(dataset => compressed.Any(configuration => run.Cells.Any(cell => cell.Name == configuration.Name && cell.Dataset == dataset.Id)))
            .ToList();

        foreach (var dataset in datasets)
        {
            Table(document, run, null, compressed, dataset.Id, $"Compressed · {dataset.Id} {dataset.Name}",
                workload => !workload.StartsWith(Evolution, StringComparison.Ordinal));
        }

        var sized = run.Datasets.Where(dataset => compressed.Any(configuration => run.PairOf(configuration.Name, dataset.Id) is not null)).ToList();

        if (sized.Count > 0)
        {
            document.Heading(3, "Compressed — payload size");
            document.Table(
                ["Dataset", .. compressed.Select(configuration => configuration.Name)],
                [.. sized.Select(dataset => (IReadOnlyList<string>)
                [
                    dataset.Id,
                    .. compressed.Select(configuration => run.PairOf(configuration.Name, dataset.Id) switch
                    {
                        { State: "Supported" or "Partial" } pair => pair.Bytes.ToString("N0", CultureInfo.InvariantCulture),
                        { } pair => pair.State,
                        null => "—",
                    }),
                ])]);
        }
    }

    private static List<(string Id, string Name, string Tiers, bool Core, string[] Types)> DatasetsOf(Run run, string tier) =>
        [.. run.Datasets.Where(dataset => dataset.Tiers.Split(' ').Contains(tier))];

    /// <summary>
    /// One comparative table: a row per configuration in roster order, a column per workload measured on
    /// the dataset, the tier in the first column. A configuration without a cell says why: the pair's
    /// verification state, the entry point it lacks, or that the run did not reach it.
    /// </summary>
    private static void Table(
        Document document,
        Run run,
        string? tier,
        IReadOnlyList<Configuration> rows,
        string dataset,
        string heading,
        Func<string, bool> workloads)
    {
        var columns = run.Workloads
            .Where(workloads)
            .Where(workload => rows.Any(configuration => Find(run, configuration, dataset, workload) is not null))
            .ToList();

        var verified = rows.Where(configuration => run.PairOf(configuration.Name, dataset) is not null).ToList();

        if (verified.Count == 0)
        {
            return;
        }

        var reference = rows.FirstOrDefault(configuration => configuration.Library == Viper);
        var notes = new List<string>();

        document.Heading(4, heading);

        if (columns.Count == 0)
        {
            document.Paragraph("No suite of this run measured this dataset.");
            return;
        }

        document.Table(
            ["Tier", "Configuration", .. columns.Select(Title)],
            [.. verified.Select(configuration =>
            {
                var pair = run.PairOf(configuration.Name, dataset)!;
                var label = configuration.Name + (pair.State == "Partial" ? " (Partial)" : string.Empty);

                if (pair.State is not ("Supported" or "Partial"))
                {
                    notes.Add($"{configuration.Name} is {pair.State}: {pair.Detail}");
                }
                else if (pair.State == "Partial")
                {
                    notes.Add($"{configuration.Name} is Partial: {pair.Detail}");
                }

                return (IReadOnlyList<string>)
                [
                    tier ?? configuration.Intended,
                    label,
                    .. columns.Select(workload => Text(run, configuration, reference, dataset, workload, pair)),
                ];
            })]);

        // The evolution read has no row of its own in the corpus; it reads the dataset it is named after.
        var types = (run.Datasets.FirstOrDefault(entry => entry.Id == dataset).Types
                     ?? run.Datasets.FirstOrDefault(entry => entry.Id == dataset.Split('/')[0]).Types
                     ?? []).ToHashSet(StringComparer.Ordinal);

        foreach (var difference in run.Differences.Where(record =>
                     types.Contains(record["type"]) && rows.Any(configuration => configuration.Family == record["family"])))
        {
            notes.Add(
                $"Model difference: {Libraries(run, difference["family"])} declares {difference["type"]} " +
                $"{difference["member"]} {difference["difference"]} — {difference["reason"]}");
        }

        if (notes.Count > 0)
        {
            document.Bullets(notes);
        }
    }

    private static ResultRow? Find(Run run, Configuration configuration, string dataset, string workload) =>
        run.Cells.FirstOrDefault(cell => cell.Name == configuration.Name && cell.Dataset == dataset && cell.Workload == workload)?.Row;

    private static string Text(Run run, Configuration configuration, Configuration? reference, string dataset, string workload, Pair pair)
    {
        if (pair.State is not ("Supported" or "Partial"))
        {
            return pair.State;
        }

        if (Find(run, configuration, dataset, workload) is not { } row)
        {
            var suite = workload[..workload.IndexOf('|', StringComparison.Ordinal)];

            return suite switch
            {
                "MarketBufferBenchmarks" when !configuration.BufferWriter => "Unsupported — no IBufferWriter entry point",
                "MarketSpanBenchmarks" when !configuration.Span => "Unsupported — no ReadOnlySpan read",
                _ => "not measured in this run",
            };
        }

        if (row.State != "Supported")
        {
            return row.State;
        }

        var text = $"{ReportWriter.Time(row.MeanNs)} ±{row.RelativeErrorPercent.ToString("F1", CultureInfo.InvariantCulture)}%";

        if (reference is not null && reference != configuration
            && Find(run, reference, dataset, workload) is { State: "Supported", MeanNs: > 0 } baseline)
        {
            text += $" ×{(row.MeanNs / baseline.MeanNs).ToString("F2", CultureInfo.InvariantCulture)}";

            if (Math.Abs(row.MeanNs - baseline.MeanNs) <= Z999 * (row.ErrorNs + baseline.ErrorNs))
            {
                text += " ≈";
            }
        }

        return $"{text} · {ReportWriter.Bytes(row.AllocatedBytes)}";
    }
}
