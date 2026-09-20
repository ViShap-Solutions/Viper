using System.Globalization;
using System.Text.Json;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// Turns the raw files of a run into <c>report.md</c>, <c>report.html</c> and the charts of §24.
/// </summary>
/// <remarks>
/// It reads the directory and nothing else, so the same command regenerates the same report from a
/// baseline committed long ago (REP-02). A cell that has no number carries its state and its reason
/// rather than a blank (REP-04), and nothing here reduces the run to a winner (REP-06) — Track A has no
/// second library to win against, and the report says so before its first number.
/// </remarks>
internal static class ReportWriter
{
    /// <summary>The ceiling STAT-04 sets on a micro-scale cell's relative margin of error.</summary>
    private const double MicroErrorCeiling = 2.0;

    /// <summary>The ceiling STAT-04 sets on a heavy cell, and the scale above which a cell is heavy.</summary>
    private const double HeavyErrorCeiling = 5.0;

    private const double HeavyThresholdNs = 1_000_000;

    internal static string Generate(string directory)
    {
        var run = RunRecord.Read(Path.Combine(directory, "run.json"));
        var results = ReadResults(directory);
        var sizes = ReadSizes(directory);

        var charts = Charts.Write(
            Path.Combine(directory, "charts"),
            results,
            sizes,
            Optional(directory, "cold-start.csv"));

        var document = new Document();

        Identity(document, run, directory);
        Caveats(document, run);
        Environment(document, directory);
        Verification(document, directory);
        Sizes(document, sizes);
        Matrix(document, results);
        Algorithms(document, results);
        Components(document, results);
        Scaling(document, results);
        System(document, directory, results);
        Gallery(document, charts);
        Caution(document, results);

        var markdown = document.ToMarkdown();
        File.WriteAllText(Path.Combine(directory, "report.md"), markdown);
        File.WriteAllText(
            Path.Combine(directory, "report.html"),
            document.ToHtml($"ViShap.Viper — {run.Label}"));

        return markdown;
    }

    /// <summary>
    /// REP-02 as a check rather than a claim: the report is generated, snapshotted, generated again and
    /// compared byte for byte. A generator that depended on anything besides the raw files — a hash the
    /// runtime randomizes per process, a timestamp, the order a dictionary happened to enumerate — fails
    /// here, instead of failing silently the day two baselines are put side by side.
    /// </summary>
    /// <returns>The files that differed between the two generations; empty when the report is a view.</returns>
    internal static IReadOnlyList<string> GenerateAndCheck(string directory)
    {
        Generate(directory);
        var first = Snapshot(directory);

        Generate(directory);
        var second = Snapshot(directory);

        return
        [
            .. first.Keys
                .Where(name => !second.TryGetValue(name, out var bytes) || !bytes.SequenceEqual(first[name]))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static Dictionary<string, byte[]> Snapshot(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var name in new[] { "report.md", "report.html" })
        {
            var path = Path.Combine(directory, name);

            if (File.Exists(path))
            {
                files[name] = File.ReadAllBytes(path);
            }
        }

        var charts = Path.Combine(directory, "charts");

        if (Directory.Exists(charts))
        {
            foreach (var path in Directory.EnumerateFiles(charts))
            {
                files[$"charts/{Path.GetFileName(path)}"] = File.ReadAllBytes(path);
            }
        }

        return files;
    }

    private static void Identity(Document document, RunRecord run, string directory)
    {
        document.Heading(1, $"ViShap.Viper — {(run.IsBaseline ? "baseline" : "measurement")} {run.Label}");

        document.Paragraph(run.IsBaseline
            ? "A frozen record of one released revision. It is never regenerated: a rebuilt baseline " +
              "agrees with whatever the code became, and so proves nothing."
            : $"**Not a baseline.** {run.Reason}. It carries only what it measured, and every figure in " +
              "it belongs to this run alone.");

        document.Table(
            ["Field", "Value"],
            [
                ["Kind", run.Kind],
                ["Track", run.Track],
                ["Tag", run.Tag ?? "none — this commit is not tagged"],
                ["git describe", $"`{run.Describe}`"],
                ["Revision", $"`{run.Revision}`"],
                [
                    "Working tree",
                    run.Dirty
                        ? run.AllowDirty
                            ? "**dirty**, allowed by `--allow-dirty`: the code measured is not exactly the "
                              + "code the revision names"
                            : "**dirty**"
                        : "clean — the code measured is the code the revision names"
                ],
                ["Job", run.Job],
                ["Filter", run.Filter is null ? "none — every suite of the track" : $"`{run.Filter}`"],
                ["Started (UTC)", run.StartedUtc],
                ["Finished (UTC)", run.FinishedUtc],
                ["Directory", $"`{Path.GetFileName(directory)}`"],
            ]);

        document.Heading(2, "How this run was produced");
        document.Code(string.Join("\n", run.Commands));
    }

    private static void Caveats(Document document, RunRecord run)
    {
        document.Heading(2, "What this record is, and what it is not");

        document.Bullets(
        [
            "**No second library was measured.** This is Track A of the benchmark plan: Viper against " +
            "Viper, across its own configurations. The competitor roster, the capability tiers and the " +
            "exclusions live in §5 and §6 of `docs/Benchmark-Plan.md`, and no cell here compares Viper " +
            "with anything but Viper.",

            "**There is no winner.** A configuration that is faster carries less; the point of the table " +
            "is what each configuration costs, not which one wins.",

            "**Payload sizes are properties of the format.** They are identical on any machine, under any " +
            "load, on any day.",

            "**Every timing and every allocation figure belongs to the machine below.** They are not " +
            "portable, and they are not comparable with a figure taken anywhere else.",

            run.SuitesNotRun.Count == 0
                ? "**Every suite of the track ran.**"
                : $"**{run.SuitesNotRun.Count} suite(s) did not run**, listed in `scope.md`.",
        ]);
    }

    private static void Environment(Document document, string directory)
    {
        var path = Path.Combine(directory, "environment.json");

        if (!File.Exists(path))
        {
            return;
        }

        document.Heading(2, "Environment");

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var rows = new List<IReadOnlyList<string>>();

        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (property.Name == "packages")
            {
                continue;
            }

            rows.Add([property.Name, property.Value.ToString()]);
        }

        document.Table(["Field", "Value"], rows);

        if (json.RootElement.TryGetProperty("packages", out var packages)
            && packages.EnumerateObject().Any())
        {
            document.Heading(3, "Pinned packages");
            document.Table(
                ["Package", "Version"],
                [.. packages.EnumerateObject().Select(package =>
                    (IReadOnlyList<string>)[package.Name, package.Value.ToString()])]);
        }
    }

    private static void Verification(Document document, string directory)
    {
        var path = Path.Combine(directory, "verification.csv");

        if (!File.Exists(path))
        {
            return;
        }

        var lines = File.ReadAllLines(path).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var states = lines
            .Select(line => line.Split(',') is { Length: > 2 } cells ? cells[2] : "unknown")
            .GroupBy(state => state)
            .OrderBy(group => group.Key)
            .ToList();

        document.Heading(2, "Verification before timing");
        document.Paragraph(
            $"{lines.Count} (adapter, dataset) pairs were serialized, read back and compared before any " +
            "suite was allowed to time them. A timing over an unverified pair is not a measurement.");

        document.Table(
            ["State", "Pairs"],
            [.. states.Select(group => (IReadOnlyList<string>)[group.Key, group.Count().ToString(CultureInfo.InvariantCulture)])]);
    }

    private static void Sizes(Document document, IReadOnlyList<SizeRow> sizes)
    {
        if (sizes.Count == 0)
        {
            return;
        }

        document.Heading(2, "Payload size and envelope accounting");
        document.Paragraph(
            "No timing appears in this table. A size is deterministic, so these rows are valid the moment " +
            "they are taken and on any machine.");

        document.Table(
            ["Profile", "Dataset", "State", "Bytes", "Envelope", "Reference framing", "Compression ratio"],
            [.. sizes.Select(row => (IReadOnlyList<string>)
            [
                row.Profile,
                row.Dataset,
                row.State,
                row.Bytes.ToString("N0", CultureInfo.InvariantCulture),
                row.EnvelopeBytes.ToString("N0", CultureInfo.InvariantCulture),
                row.ReferenceFramingBytes.ToString("N0", CultureInfo.InvariantCulture),
                row.CompressionRatio.ToString("F4", CultureInfo.InvariantCulture),
            ])]);
    }

    private static void Matrix(Document document, IReadOnlyList<ResultRow> results)
    {
        Suite(document, results,
            "The profile matrix",
            "Every configuration of §8 over the core corpus, through the buffered and the stream entry " +
            "points. The floor row is an adapter that does none of a serializer's work: a cell near it is " +
            "a harness artifact rather than a fast serializer.",
            ["ProfileMatrixBenchmarks", "ProfileStreamBenchmarks", "HarnessFloorBenchmarks"]);
    }

    private static void Algorithms(Document document, IReadOnlyList<ResultRow> results) =>
        Suite(document, results,
            "Compression, checksum and encryption",
            "Each phase measured against the same datasets with no algorithm configured, so a phase's " +
            "cost is a difference rather than an estimate.",
            ["AlgorithmBenchmarks", "EnvelopeDifferentialBenchmarks"]);

    private static void Components(Document document, IReadOnlyList<ResultRow> results)
    {
        var rows = results.Where(row => row.IsComponent).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        document.Heading(2, "Component record");
        document.Paragraph(
            "Diagnostic, never a market comparison. These figures exist for two readers: the engineer " +
            "explaining an end-to-end number, and the next version, which needs a per-mechanism record of " +
            "this one to know what a change actually improved.");

        foreach (var suite in rows.GroupBy(row => row.SuiteName).OrderBy(group => group.Key))
        {
            document.Heading(3, suite.Key);
            document.Table(Headers, [.. suite.Select(Cells)]);
        }
    }

    private static void Scaling(Document document, IReadOnlyList<ResultRow> results) =>
        Suite(document, results,
            "Scaling curves",
            "A single size is a point; a curve is a property. Each sweep multiplies rather than adds, so " +
            "the charts draw them on a logarithmic axis.",
            [
                "ElementCountScalingBenchmarks", "PayloadSizeScalingBenchmarks", "DepthScalingBenchmarks",
                "StringScalingBenchmarks", "DictionaryScalingBenchmarks", "SharingDensityBenchmarks",
                "MemberCountScalingBenchmarks",
            ]);

    private static void System(Document document, string directory, IReadOnlyList<ResultRow> results)
    {
        Suite(document, results, "Concurrency", "Real threads over one shared serializer.", ["ConcurrencyBenchmarks"]);

        foreach (var (file, heading, note) in new[]
        {
            ("cold-start.csv", "Cold start", "One process per measurement. A first in-process call after a warm-up is not cold."),
            ("contract-cold.csv", "Member-plan construction", "What a type costs the first time it is seen. The first row of the table also pays the one-time JIT of the construction path."),
            ("soak.csv", "Sustained load", "Throughput and memory sampled throughout a fixed duration."),
        })
        {
            var path = Path.Combine(directory, file);

            if (!File.Exists(path))
            {
                continue;
            }

            var lines = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

            if (lines.Count < 2)
            {
                continue;
            }

            document.Heading(2, heading);
            document.Paragraph(note);
            document.Table(
                [.. lines[0].Split(',')],
                [.. lines.Skip(1).Select(line => (IReadOnlyList<string>)[.. line.Split(',')])]);
        }
    }

    private static void Gallery(Document document, IReadOnlyList<Chart> charts)
    {
        if (charts.Count == 0)
        {
            return;
        }

        document.Heading(2, "Charts");
        document.Paragraph(
            "Generated from the files above and from nothing else. Every chart names its units and says " +
            "whether lower or higher is better.");

        foreach (var chart in charts)
        {
            document.Image($"charts/{chart.File}", $"{chart.Title} — {chart.Note}");
        }
    }

    /// <summary>
    /// The cells a reader should not take at face value: one whose margin of error exceeds the ceiling
    /// STAT-04 sets, and one that produced no number at all.
    /// </summary>
    private static void Caution(Document document, IReadOnlyList<ResultRow> results)
    {
        var failed = results.Where(row => row.State != "Supported").ToList();

        var noisy = results
            .Where(row => row.State == "Supported" && row.MeanNs > 0)
            .Where(row => row.RelativeErrorPercent >
                          (row.MeanNs >= HeavyThresholdNs ? HeavyErrorCeiling : MicroErrorCeiling))
            .OrderByDescending(row => row.RelativeErrorPercent)
            .ToList();

        document.Heading(2, "Cells to read with caution");

        if (failed.Count == 0 && noisy.Count == 0)
        {
            document.Paragraph(
                "None. Every cell produced a number, and every margin of error is inside the ceiling the " +
                "plan sets: 2% for a micro-scale cell, 5% for a heavy one.");

            return;
        }

        if (failed.Count > 0)
        {
            document.Heading(3, "Produced no number");
            document.Table(
                ["Suite", "Method", "Parameters", "State"],
                [.. failed.Select(row => (IReadOnlyList<string>)[row.SuiteName, row.Method, row.Parameters, row.State])]);
        }

        if (noisy.Count > 0)
        {
            document.Heading(3, "Noisy");
            document.Paragraph(
                "Published with the margin the run produced, and never re-run until the number looked " +
                "prettier.");

            document.Table(
                ["Suite", "Method", "Parameters", "Mean", "Margin"],
                [.. noisy.Select(row => (IReadOnlyList<string>)
                [
                    row.SuiteName,
                    row.Method,
                    row.Parameters,
                    Time(row.MeanNs),
                    $"±{row.RelativeErrorPercent.ToString("F1", CultureInfo.InvariantCulture)}%",
                ])]);
        }
    }

    private static void Suite(
        Document document,
        IReadOnlyList<ResultRow> results,
        string heading,
        string note,
        IReadOnlyList<string> suites)
    {
        var rows = results.Where(row => suites.Contains(row.SuiteName, StringComparer.Ordinal)).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        document.Heading(2, heading);
        document.Paragraph(note);

        foreach (var suite in rows.GroupBy(row => row.SuiteName)
                     .OrderBy(group => suites.ToList().IndexOf(group.Key)))
        {
            document.Heading(3, suite.Key);
            document.Table(Headers, [.. suite.Select(Cells)]);
        }
    }

    private static readonly string[] Headers =
        ["Method", "Parameters", "State", "Mean", "Margin", "Median", "P95", "Ops/s", "Allocated", "Gen0/1k"];

    private static IReadOnlyList<string> Cells(ResultRow row) =>
    [
        row.Method,
        row.Parameters,
        row.State,
        Time(row.MeanNs),
        $"±{row.RelativeErrorPercent.ToString("F1", CultureInfo.InvariantCulture)}%",
        Time(row.MedianNs),
        Time(row.P95Ns),
        row.OpsPerSecond.ToString("N0", CultureInfo.InvariantCulture),
        Bytes(row.AllocatedBytes),
        row.Gen0Per1000.ToString("F3", CultureInfo.InvariantCulture),
    ];

    /// <summary>A duration at the scale it belongs to, so a table does not print 0.000 or 12345678.9.</summary>
    private static string Time(double nanoseconds) => nanoseconds switch
    {
        0 => "—",
        < 1_000 => $"{nanoseconds.ToString("F2", CultureInfo.InvariantCulture)} ns",
        < 1_000_000 => $"{(nanoseconds / 1_000).ToString("F2", CultureInfo.InvariantCulture)} µs",
        < 1_000_000_000 => $"{(nanoseconds / 1_000_000).ToString("F2", CultureInfo.InvariantCulture)} ms",
        _ => $"{(nanoseconds / 1_000_000_000).ToString("F3", CultureInfo.InvariantCulture)} s",
    };

    private static string Bytes(double bytes) => bytes switch
    {
        0 => "—",
        < 1_024 => $"{bytes.ToString("F0", CultureInfo.InvariantCulture)} B",
        < 1_048_576 => $"{(bytes / 1_024).ToString("F2", CultureInfo.InvariantCulture)} KB",
        _ => $"{(bytes / 1_048_576).ToString("F2", CultureInfo.InvariantCulture)} MB",
    };

    private static IReadOnlyList<ResultRow> ReadResults(string directory)
    {
        var path = Path.Combine(directory, "results.csv");
        return File.Exists(path) ? ResultRow.Read(path) : [];
    }

    private static IReadOnlyList<SizeRow> ReadSizes(string directory)
    {
        var path = Path.Combine(directory, "payload-sizes.csv");
        return File.Exists(path) ? SizeReport.Read(path) : [];
    }

    private static string? Optional(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? path : null;
    }
}
