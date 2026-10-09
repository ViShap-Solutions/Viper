using System.Globalization;
using System.Text;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// BASE-02 — the delta of a run against a baseline, cell by cell, with margins of error. A cell is one
/// row of a raw file, keyed as the baseline keys it; the two are read from their committed raw files
/// and nothing is recomputed. A timed cell is faster or slower only when its interval — the mean plus
/// or minus its error — does not overlap the baseline's; otherwise it is within error, whatever its
/// ratio says.
/// </summary>
/// <remarks>
/// Written beside the run as <c>comparison-&lt;baseline&gt;.md</c> for reading and
/// <c>comparison-&lt;baseline&gt;.csv</c> for tools. A cell present on one side only is listed, never
/// dropped: a renamed benchmark shows up as one missing cell and one new cell.
/// </remarks>
internal static class BaselineComparison
{
    private static readonly string[] TimedFiles = ["results.csv", "components.csv"];

    internal static int Run(string baselineDirectory, string runDirectory, string? output)
    {
        foreach (var directory in new[] { baselineDirectory, runDirectory })
        {
            if (!Directory.Exists(directory))
            {
                Console.Error.WriteLine($"No run directory at '{directory}'.");
                return 1;
            }
        }

        string baselineName = Path.GetFileName(Path.TrimEndingDirectorySeparator(baselineDirectory));
        string runName = Path.GetFileName(Path.TrimEndingDirectorySeparator(runDirectory));

        var rows = new List<Row>();
        var markdown = new StringBuilder();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"# {runName} against {baselineName}");
        markdown.AppendLine();
        markdown.AppendLine(
            "A timed cell is `faster` or `slower` only when its interval, mean ± error, does not overlap " +
            "the baseline's; otherwise it is `within error`. Ratio is run ÷ baseline.");

        foreach (var file in TimedFiles)
        {
            Section(markdown, file, CompareTimed(file, baselineDirectory, runDirectory), rows);
        }

        Section(markdown, "contract-cold.csv", CompareContractCold(baselineDirectory, runDirectory), rows);
        Section(markdown, "cold-start.csv", CompareColdStart(baselineDirectory, runDirectory), rows);
        Section(markdown, "first-use.csv", CompareFirstUse(baselineDirectory, runDirectory), rows);

        string markdownPath = output ?? Path.Combine(runDirectory, $"comparison-{baselineName}.md");
        File.WriteAllText(markdownPath, markdown.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.ChangeExtension(markdownPath, ".csv"), Csv(rows), Encoding.UTF8);

        foreach (var group in rows.GroupBy(row => row.File))
        {
            Console.WriteLine(
                $"{group.Key,-18} {group.Count(),5} cells: " +
                string.Join(", ", group.GroupBy(row => row.Verdict).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(verdict => $"{verdict.Count()} {verdict.Key}")));
        }

        Console.WriteLine($"Written to {markdownPath}");
        return 0;
    }

    /// <summary>One compared cell, as the csv output carries it.</summary>
    private sealed record Row(
        string File,
        string Cell,
        string Baseline,
        string Run,
        string Ratio,
        string BaselineAllocated,
        string RunAllocated,
        string Verdict);

    private static IReadOnlyList<Row> CompareTimed(string file, string baselineDirectory, string runDirectory)
    {
        var baseline = Read(Path.Combine(baselineDirectory, file), TimedKey);
        var run = Read(Path.Combine(runDirectory, file), TimedKey);
        var rows = new List<Row>();

        // A run narrowed by a filter holds some suites and not others; a suite it did not run is not
        // compared cell by cell but reported once.
        var measured = run.Keys.Select(Suite).ToHashSet(StringComparer.Ordinal);

        foreach (var suite in baseline.Keys.Select(Suite).Distinct().Where(suite => !measured.Contains(suite))
                     .Order(StringComparer.Ordinal))
        {
            rows.Add(new Row(file, suite, "", "", "", "", "", NotMeasured));
        }

        foreach (var key in baseline.Keys.Union(run.Keys).Where(key => measured.Contains(Suite(key)))
                     .Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var before);
            run.TryGetValue(key, out var after);

            if (before is null || after is null)
            {
                rows.Add(OneSided(file, key, before, after, cell => Timing(cell), cell => cell["allocated_bytes"]));
                continue;
            }

            double meanBefore = Number(before["mean_ns"]), errorBefore = Number(before["error_ns"]);
            double meanAfter = Number(after["mean_ns"]), errorAfter = Number(after["error_ns"]);

            string verdict = before["state"] != "Supported" || after["state"] != "Supported"
                ? $"state {before["state"]} → {after["state"]}"
                : Interval(meanBefore - errorBefore, meanBefore + errorBefore, meanAfter - errorAfter, meanAfter + errorAfter);

            if (before["job"] != after["job"])
            {
                verdict += $" (job {before["job"]} → {after["job"]})";
            }

            rows.Add(new Row(
                file, key, Timing(before), Timing(after), Ratio(meanAfter, meanBefore),
                before["allocated_bytes"], after["allocated_bytes"], verdict));
        }

        return rows;
    }

    private static IReadOnlyList<Row> CompareContractCold(string baselineDirectory, string runDirectory)
    {
        const string file = "contract-cold.csv";
        var baseline = Read(Path.Combine(baselineDirectory, file), cell => $"{cell["kind"]} | {cell["type"]}");
        var run = Read(Path.Combine(runDirectory, file), cell => $"{cell["kind"]} | {cell["type"]}");
        var rows = new List<Row>();

        foreach (var key in baseline.Keys.Union(run.Keys).Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var before);
            run.TryGetValue(key, out var after);

            if (before is null || after is null)
            {
                rows.Add(OneSided(file, key, before, after, Median, cell => cell["allocated_bytes_median"]));
                continue;
            }

            int samples = Math.Min(int.Parse(before["samples"], CultureInfo.InvariantCulture),
                int.Parse(after["samples"], CultureInfo.InvariantCulture));

            // A distribution is read by its median against the other's spread from median to p95; a
            // single observation has no spread and is reported as one.
            string verdict = samples == 1
                ? "single observation"
                : Interval(
                    Number(before["min_us"]), Number(before["p95_us"]),
                    Number(after["min_us"]), Number(after["p95_us"]));

            rows.Add(new Row(
                file, key, Median(before), Median(after),
                Ratio(Number(after["median_us"]), Number(before["median_us"])),
                before["allocated_bytes_median"], after["allocated_bytes_median"], verdict));
        }

        return rows;
    }

    private static IReadOnlyList<Row> CompareColdStart(string baselineDirectory, string runDirectory)
    {
        const string file = "cold-start.csv";
        static string Key(Dictionary<string, string> cell) =>
            $"{cell["profile"]} | {cell["dataset"]} | {cell["operation"]}";

        var baseline = Read(Path.Combine(baselineDirectory, file), Key);
        var run = Read(Path.Combine(runDirectory, file), Key);
        var rows = new List<Row>();

        static string Operation(Dictionary<string, string> cell) =>
            $"{cell["operation_ms_median"]} ms [{cell["operation_ms_min"]}, {cell["operation_ms_max"]}]";

        foreach (var key in baseline.Keys.Union(run.Keys).Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var before);
            run.TryGetValue(key, out var after);

            if (before is null || after is null)
            {
                rows.Add(OneSided(file, key, before, after, Operation, cell => cell["allocated_bytes_median"]));
                continue;
            }

            rows.Add(new Row(
                file, key, Operation(before), Operation(after),
                Ratio(Number(after["operation_ms_median"]), Number(before["operation_ms_median"])),
                before["allocated_bytes_median"], after["allocated_bytes_median"],
                Interval(
                    Number(before["operation_ms_min"]), Number(before["operation_ms_max"]),
                    Number(after["operation_ms_min"]), Number(after["operation_ms_max"]))));
        }

        return rows;
    }

    private static IReadOnlyList<Row> CompareFirstUse(string baselineDirectory, string runDirectory)
    {
        const string file = "first-use.csv";
        static string Key(Dictionary<string, string> cell) =>
            $"{cell["profile"]} | {cell["type"]} | {cell["operation"]}";

        var baseline = Read(Path.Combine(baselineDirectory, file), Key);
        var run = Read(Path.Combine(runDirectory, file), Key);
        var rows = new List<Row>();

        static string Use(Dictionary<string, string> cell) =>
            $"{cell["median_us"]} µs [{cell["min_us"]}, {cell["max_us"]}]";

        foreach (var key in baseline.Keys.Union(run.Keys).Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var before);
            run.TryGetValue(key, out var after);

            if (before is null || after is null)
            {
                rows.Add(OneSided(file, key, before, after, Use, cell => cell["allocated_bytes_median"]));
                continue;
            }

            rows.Add(new Row(
                file, key, Use(before), Use(after),
                Ratio(Number(after["median_us"]), Number(before["median_us"])),
                before["allocated_bytes_median"], after["allocated_bytes_median"],
                Interval(
                    Number(before["min_us"]), Number(before["max_us"]),
                    Number(after["min_us"]), Number(after["max_us"]))));
        }

        return rows;
    }

    /// <summary>Faster, slower or within error, from two intervals that either overlap or do not.</summary>
    private static string Interval(double baselineLow, double baselineHigh, double runLow, double runHigh) =>
        runHigh < baselineLow ? "faster" : runLow > baselineHigh ? "slower" : "within error";

    private static Row OneSided(
        string file,
        string key,
        Dictionary<string, string>? before,
        Dictionary<string, string>? after,
        Func<Dictionary<string, string>, string> value,
        Func<Dictionary<string, string>, string> allocated) =>
        new(
            file, key,
            before is null ? "" : value(before),
            after is null ? "" : value(after),
            "",
            before is null ? "" : allocated(before),
            after is null ? "" : allocated(after),
            before is null ? "run only" : "baseline only");

    private const string NotMeasured = "suite not measured";

    private static string Suite(string key) => key[..key.IndexOf(" | ", StringComparison.Ordinal)];

    private static string TimedKey(Dictionary<string, string> cell)
    {
        string suite = cell["suite"];
        suite = suite[(suite.LastIndexOf('.') + 1)..];
        return $"{suite} | {cell["method"]} | {cell["parameters"]}";
    }

    private static string Timing(Dictionary<string, string> cell) =>
        $"{Number(cell["mean_ns"]).ToString("F1", CultureInfo.InvariantCulture)} ± " +
        $"{Number(cell["error_ns"]).ToString("F1", CultureInfo.InvariantCulture)} ns";

    private static string Median(Dictionary<string, string> cell) =>
        $"{cell["median_us"]} µs (p95 {cell["p95_us"]}, n={cell["samples"]})";

    private static string Ratio(double run, double baseline) =>
        baseline == 0 ? "" : (run / baseline).ToString("F3", CultureInfo.InvariantCulture);

    private static double Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;

    private static void Section(StringBuilder markdown, string file, IReadOnlyList<Row> section, List<Row> rows)
    {
        rows.AddRange(section);

        markdown.AppendLine();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"## {file}");
        markdown.AppendLine();

        if (section.Count == 0)
        {
            markdown.AppendLine("Neither run holds this file.");
            return;
        }

        var unmeasured = section.Where(row => row.Verdict == NotMeasured).Select(row => row.Cell).ToArray();
        if (unmeasured.Length > 0)
        {
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"Suites of the baseline this run did not measure: {string.Join(", ", unmeasured)}.");
            markdown.AppendLine();
        }

        markdown.AppendLine("| cell | baseline | run | ratio | baseline B | run B | verdict |");
        markdown.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var row in section.Where(row => row.Verdict != NotMeasured))
        {
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {Escape(row.Cell)} | {row.Baseline} | {row.Run} | {row.Ratio} | {row.BaselineAllocated} | " +
                $"{row.RunAllocated} | {row.Verdict} |");
        }
    }

    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Csv(IEnumerable<Row> rows)
    {
        var csv = new StringBuilder("file,cell,baseline,run,ratio,baseline_allocated_bytes,run_allocated_bytes,verdict\n");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                row.File, row.Cell, row.Baseline, row.Run, row.Ratio, row.BaselineAllocated, row.RunAllocated, row.Verdict
            }.Select(Quote)));
        }

        return csv.ToString();
    }

    private static string Quote(string field) => $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>The cells of one raw file, keyed; an absent file is an empty table.</summary>
    private static Dictionary<string, Dictionary<string, string>> Read(
        string path,
        Func<Dictionary<string, string>, string> key)
    {
        var cells = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return cells;
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8).Where(line => line.Length > 0).ToArray();
        if (lines.Length == 0)
        {
            return cells;
        }

        var header = Fields(lines[0].TrimStart('﻿'));
        foreach (var line in lines.Skip(1))
        {
            var values = Fields(line);
            var cell = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < header.Count; index++)
            {
                cell[header[index]] = index < values.Count ? values[index] : "";
            }

            cells[key(cell)] = cell;
        }

        return cells;
    }

    /// <summary>The fields of one csv line, with double-quoted fields unquoted.</summary>
    private static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (quoted)
            {
                if (current == '"' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (current == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(current);
                }
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(current);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
