using System.Globalization;
using System.Text;
using BenchmarkDotNet.Reports;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// One timed cell, as it is written to <c>results.csv</c> and read back by the report generator.
/// </summary>
/// <remarks>
/// Times are nanoseconds and allocation is bytes per operation, both as BenchmarkDotNet measured them.
/// <c>State</c> carries the result state of Benchmark-Plan §1: a cell that did not produce a number
/// says so rather than being left blank (REP-04).
/// </remarks>
internal sealed record ResultRow(
    string Suite,
    string Method,
    string Parameters,
    string Job,
    string State,
    double MeanNs,
    double ErrorNs,
    double StdDevNs,
    double MedianNs,
    double P95Ns,
    double OpsPerSecond,
    double AllocatedBytes,
    double Gen0Per1000,
    double Gen1Per1000,
    double Gen2Per1000,
    int Iterations)
{
    private const string Header =
        "suite,method,parameters,job,state,mean_ns,error_ns,stddev_ns,median_ns,p95_ns," +
        "ops_per_second,allocated_bytes,gen0_per_1000,gen1_per_1000,gen2_per_1000,iterations";

    /// <summary>The relative margin of error, which STAT-04 bounds and the report prints beside a cell.</summary>
    internal double RelativeErrorPercent => MeanNs > 0 ? ErrorNs / MeanNs * 100 : 0;

    /// <summary>True for a component cell, which is diagnostic and never enters a market table (§18).</summary>
    internal bool IsComponent => Suite.Contains("Components", StringComparison.Ordinal);

    /// <summary>The short name of the suite, without the namespace that prefixes it in the table.</summary>
    internal string SuiteName => Suite[(Suite.LastIndexOf('.') + 1)..];

    /// <summary>
    /// The value of one parameter of this cell, or <see langword="null"/> when the suite has no such
    /// parameter. Charts select their points this way rather than by parsing a display string.
    /// </summary>
    internal string? Parameter(string name)
    {
        foreach (var pair in Parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int split = pair.IndexOf('=', StringComparison.Ordinal);

            if (split > 0 && pair.AsSpan(0, split).Trim().SequenceEqual(name))
            {
                return pair[(split + 1)..].Trim();
            }
        }

        return null;
    }

    internal static IReadOnlyList<ResultRow> From(IEnumerable<Summary> summaries)
    {
        var rows = new List<ResultRow>();

        foreach (var summary in summaries)
        {
            foreach (var report in summary.Reports)
            {
                var descriptor = report.BenchmarkCase.Descriptor;
                var statistics = report.ResultStatistics;
                var gc = report.GcStats;
                double operations = gc.TotalOperations > 0 ? gc.TotalOperations : 1;

                rows.Add(new ResultRow(
                    Suite: descriptor.Type.FullName ?? descriptor.Type.Name,
                    Method: descriptor.WorkloadMethodDisplayInfo,
                    Parameters: Describe(report.BenchmarkCase.Parameters),
                    Job: report.BenchmarkCase.Job.ResolvedId,
                    State: report.Success && statistics is not null ? "Supported" : "Failed",
                    MeanNs: statistics?.Mean ?? 0,
                    ErrorNs: statistics?.StandardError ?? 0,
                    StdDevNs: statistics?.StandardDeviation ?? 0,
                    MedianNs: statistics?.Median ?? 0,
                    P95Ns: statistics?.Percentiles.P95 ?? 0,
                    OpsPerSecond: statistics is { Mean: > 0 } ? 1_000_000_000d / statistics.Mean : 0,
                    AllocatedBytes: gc.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0,
                    Gen0Per1000: gc.Gen0Collections / operations * 1000,
                    Gen1Per1000: gc.Gen1Collections / operations * 1000,
                    Gen2Per1000: gc.Gen2Collections / operations * 1000,
                    Iterations: statistics?.N ?? 0));
            }
        }

        return rows;
    }

    internal static void Write(IReadOnlyList<ResultRow> rows, string path)
    {
        var csv = new StringBuilder(Header).Append('\n');

        foreach (var row in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{Quote(row.Suite)},{Quote(row.Method)},{Quote(row.Parameters)},{Quote(row.Job)},{row.State},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.MeanNs)},{N(row.ErrorNs)},{N(row.StdDevNs)},{N(row.MedianNs)},{N(row.P95Ns)},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.OpsPerSecond)},{N(row.AllocatedBytes)},{N(row.Gen0Per1000)},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.Gen1Per1000)},{N(row.Gen2Per1000)},{row.Iterations}\n");
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Reads the table back. The report is a view over this file rather than over the run, so it can be
    /// regenerated from a committed baseline long after the process that measured it exited (REP-02).
    /// </summary>
    internal static IReadOnlyList<ResultRow> Read(string path)
    {
        var rows = new List<ResultRow>();
        var lines = File.ReadAllLines(path);

        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var cells = Split(lines[index]);

            rows.Add(new ResultRow(
                cells[0], cells[1], cells[2], cells[3], cells[4],
                D(cells[5]), D(cells[6]), D(cells[7]), D(cells[8]), D(cells[9]),
                D(cells[10]), D(cells[11]), D(cells[12]), D(cells[13]), D(cells[14]),
                int.Parse(cells[15], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    /// <summary>The allocation projection of §13, written without the timings beside it.</summary>
    internal static void WriteMemory(IReadOnlyList<ResultRow> rows, string path)
    {
        var csv = new StringBuilder(
            "suite,method,parameters,allocated_bytes,gen0_per_1000,gen1_per_1000,gen2_per_1000\n");

        foreach (var row in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{Quote(row.Suite)},{Quote(row.Method)},{Quote(row.Parameters)},{N(row.AllocatedBytes)},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.Gen0Per1000)},{N(row.Gen1Per1000)},{N(row.Gen2Per1000)}\n");
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// The parameters of a case as <c>name=value</c> pairs separated by semicolons, written by this
    /// harness rather than taken from a BenchmarkDotNet display string, so a chart can read one back.
    /// </summary>
    private static string Describe(BenchmarkDotNet.Parameters.ParameterInstances parameters) =>
        string.Join("; ", parameters.Items.Select(item => $"{item.Name}={item.Value}"));

    private static string N(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>A parameter list carries commas, so every text cell is quoted.</summary>
    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string[] Split(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            char character = line[index];

            if (quoted)
            {
                if (character != '"')
                {
                    cell.Append(character);
                }
                else if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;

                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;

                default:
                    cell.Append(character);
                    break;
            }
        }

        cells.Add(cell.ToString());
        return [.. cells];
    }
}
