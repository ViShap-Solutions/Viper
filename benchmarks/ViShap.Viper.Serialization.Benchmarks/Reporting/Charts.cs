using System.Globalization;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>One chart produced by a run, as the report lists it.</summary>
internal sealed record Chart(string File, string Title, string Note);

/// <summary>
/// The charts of Benchmark-Plan §24, generated from the raw files of the run and from nothing else
/// (CHT-13). A chart whose suite did not run is not drawn at all rather than drawn empty, so a partial
/// run produces fewer charts and never a misleading one.
/// </summary>
internal static class Charts
{
    internal static IReadOnlyList<Chart> Write(
        string directory,
        IReadOnlyList<ResultRow> results,
        IReadOnlyList<SizeRow> sizes,
        string? coldStartCsv)
    {
        Directory.CreateDirectory(directory);

        var charts = new List<Chart>();

        void Emit(string file, string title, string note, string svg)
        {
            File.WriteAllText(Path.Combine(directory, file), svg);
            charts.Add(new Chart(file, title, note));
        }

        // CHT-01, CHT-02, CHT-03, CHT-04 — the profile matrix, one chart per metric.
        var matrix = results
            .Where(row => row.SuiteName == "ProfileMatrixBenchmarks" && row.State == "Supported")
            .ToList();

        foreach (var (methodPart, metric, unit, lower, selector, file) in new (string, string, string, bool, Func<ResultRow, double>, string)[]
        {
            ("WL-01", "Serialize time", "nanoseconds per operation", true, row => row.MeanNs, "serialize-time.svg"),
            ("WL-04", "Deserialize time", "nanoseconds per operation", true, row => row.MeanNs, "deserialize-time.svg"),
            ("WL-01", "Serialize throughput", "operations per second", false, row => row.OpsPerSecond, "serialize-throughput.svg"),
            ("WL-01", "Serialize allocation", "bytes allocated per operation", true, row => row.AllocatedBytes, "serialize-allocation.svg"),
            ("WL-01", "Serialize Gen0 pressure", "Gen0 collections per 1000 operations", true, row => row.Gen0Per1000, "serialize-gc.svg"),
        })
        {
            var rows = matrix.Where(row => row.Method.Contains(methodPart, StringComparison.Ordinal)).ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            var chart = new BarChart($"{metric} — Viper profiles over the core corpus", unit, lower);

            foreach (var row in rows.OrderBy(row => row.Parameter("Data")).ThenBy(row => row.Parameter("Profile")))
            {
                chart.Add(
                    $"{row.Parameter("Profile")} · {row.Parameter("Data")}",
                    selector(row),
                    Colour(row.Parameter("Profile")));
            }

            Emit(file, $"{metric}, profile matrix", $"{rows.Count} cells, {unit}.", chart.Render());
        }

        // CHT-05 — payload size, which is a property of the format and carries no timing beside it.
        if (sizes.Count > 0)
        {
            var chart = new BarChart("Payload size — Viper profiles over the corpus", "bytes", true);

            foreach (var row in sizes.Where(row => row.State == "Supported" && row.Bytes > 0)
                         .OrderBy(row => row.Dataset).ThenBy(row => row.Profile))
            {
                chart.Add($"{row.Profile} · {row.Dataset}", row.Bytes, Colour(row.Profile));
            }

            Emit("payload-size.svg", "Payload size", "Deterministic: the same bytes on any machine.", chart.Render());

            // CHT-06 — the compression ratio, where a phase actually applies.
            var compressed = sizes
                .Where(row => Math.Abs(row.CompressionRatio - 1.0) > 0.0001)
                .OrderBy(row => row.Dataset)
                .ThenBy(row => row.Profile)
                .ToList();

            if (compressed.Count > 0)
            {
                var ratio = new BarChart(
                    "Compression ratio — compressed size over uncompressed", "ratio, 1.0 is no saving", true);

                foreach (var row in compressed)
                {
                    ratio.Add($"{row.Profile} · {row.Dataset}", row.CompressionRatio, Colour(row.Profile));
                }

                Emit("compression-ratio.svg", "Compression ratio", "Lower means the codec saved more.", ratio.Render());
            }
        }

        // CHT-09 — the scaling curves of §19, one chart per sweep.
        foreach (var (suite, parameter, xLabel, file, title) in new[]
        {
            ("ElementCountScalingBenchmarks", "Count", "records in the batch", "scaling-elements.svg", "SCALE-01 element count"),
            ("PayloadSizeScalingBenchmarks", "Bytes", "payload bytes", "scaling-payload.svg", "SCALE-02 payload size"),
            ("DepthScalingBenchmarks", "Depth", "nesting levels", "scaling-depth.svg", "SCALE-03 depth"),
            ("MemberCountScalingBenchmarks", "Members", "members on the type", "scaling-members.svg", "SCALE-04 member count"),
            ("StringScalingBenchmarks", "Length", "characters in the string", "scaling-strings.svg", "SCALE-05 string length"),
            ("DictionaryScalingBenchmarks", "Entries", "dictionary entries", "scaling-dictionary.svg", "SCALE-06 dictionary size"),
            ("SharingDensityBenchmarks", "SharedPercent", "percent of nodes shared", "scaling-sharing.svg", "SCALE-07 sharing density"),
        })
        {
            var rows = results
                .Where(row => row.SuiteName == suite && row.State == "Supported" && row.Parameter(parameter) is not null)
                .ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            var chart = new LineChart($"{title} — time per operation", xLabel, "nanoseconds per operation");

            // The job is not a parameter, so a suite measured under two of them — SCALE-09 runs the payload
            // curve under Server GC as well — would otherwise put both passes in one series, with two values
            // at every x and neither of them labelled.
            bool manyJobs = rows.Select(row => row.Job).Distinct().Count() > 1;

            // A series is one method under one set of the other parameters, so a curve never mixes two.
            foreach (var series in rows.GroupBy(row => SeriesName(row, parameter, manyJobs)).OrderBy(group => group.Key))
            {
                chart.Add(
                    series.Key,
                    series.Select(row => (X(row.Parameter(parameter)!), row.MeanNs)));
            }

            Emit(file, title, "Logarithmic x axis; a straight line means the growth is proportional.", chart.Render());
        }

        // CHT-08 — cold start against steady state, from the process-per-measurement runner of §20.
        if (coldStartCsv is not null && File.Exists(coldStartCsv))
        {
            var chart = new BarChart(
                "Cold start — first operation in a fresh process", "milliseconds", true);

            foreach (var line in File.ReadAllLines(coldStartCsv).Skip(1))
            {
                var cells = line.Split(',');

                if (cells.Length < 7)
                {
                    continue;
                }

                chart.Add(
                    $"{cells[0]} · {cells[1]} · {cells[2]}",
                    double.Parse(cells[6], CultureInfo.InvariantCulture),
                    Colour(cells[0]));
            }

            Emit("cold-start.svg", "Cold start", "Median first operation over many process launches.", chart.Render());
        }

        // §18 — two component charts, the ones that explain the most end-to-end cells.
        foreach (var (suite, title, file, unit) in new[]
        {
            ("ValuePrimitiveBenchmarks", "MICRO-01 payload primitives", "components-primitives.svg", "nanoseconds per call"),
            ("StreamMechanismBenchmarks", "MICRO-09 stream mechanisms", "components-streams.svg", "nanoseconds per call"),
        })
        {
            var rows = results.Where(row => row.SuiteName == suite && row.State == "Supported").ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            var chart = new BarChart($"{title} — measured on the internal type", unit, true);

            foreach (var row in rows.OrderBy(row => row.Method).ThenBy(row => row.Parameters))
            {
                chart.Add(Label(row), row.MeanNs, 2);
            }

            Emit(file, title, "Diagnostic. Never a market comparison.", chart.Render());
        }

        return charts;
    }

    /// <summary>
    /// A stable colour per label, so one profile keeps its colour across every chart and across every
    /// run. The hash is computed here rather than taken from <c>string.GetHashCode</c>, which .NET
    /// randomizes per process: a chart whose colours moved between two runs would break the byte-identical
    /// regeneration REP-02 asks for.
    /// </summary>
    private static int Colour(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return 0;
        }

        // FNV-1a over the UTF-16 units, which depends on the text alone.
        uint hash = 2166136261;

        foreach (char character in label)
        {
            hash = (hash ^ character) * 16777619;
        }

        return (int)(hash % (uint)Svg.Series.Length);
    }

    private static string Label(ResultRow row) =>
        string.IsNullOrEmpty(row.Parameters) ? row.Method : $"{row.Method} · {row.Parameters}";

    /// <summary>
    /// The series a cell belongs to: its method, every parameter except the swept one, and the job when
    /// the suite was measured under more than one.
    /// </summary>
    private static string SeriesName(ResultRow row, string swept, bool includeJob)
    {
        var parts = row.Parameters
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(pair => !pair.StartsWith(swept + "=", StringComparison.Ordinal))
            .ToList();

        if (includeJob)
        {
            // The resolved id carries the whole job description; the name alone identifies the pass.
            parts.Insert(0, row.Job.Split('(')[0]);
        }

        return parts.Count == 0 ? row.Method : $"{row.Method} · {string.Join(", ", parts)}";
    }

    private static double X(string value) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out double parsed) ? Math.Max(parsed, 1) : 1;
}
