namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// The charts of Track B (§24): for each tier and each representative dataset, time per operation with
/// its 99.9 % interval and allocation per operation, for the tier's members alone (CHT-01, CHT-03,
/// CHT-11); and the same for T4, the protected envelope against the composed baselines that do the same
/// work (CHT-07). Bars keep the roster's order, never a ranking, and every chart names its tier, its
/// dataset, its unit and which direction is better.
/// </summary>
internal static partial class MarketReport
{
    private const string BufferedSuite = "MarketBufferedBenchmarks";

    /// <summary>
    /// The datasets a chart is drawn for: the ordinary object, a small batch and a large one. The tables
    /// hold every dataset; a chart per dataset per tier would be a gallery nobody reads.
    /// </summary>
    private static readonly string[] ChartDatasets = ["DATA-02", "DATA-03", "DATA-04"];

    private static readonly (string Method, string Label)[] ChartWorkloads = [("WL-01", "serialize"), ("WL-04", "deserialize")];

    private static IReadOnlyList<Chart> WriteCharts(string directory, Run run, ProtectedRun? protectedRun)
    {
        var folder = Path.Combine(directory, "charts");
        var charts = new List<Chart>();

        foreach (var tier in Tiers)
        {
            var members = run.Members(tier).Where(configuration => !configuration.Compressed).ToList();

            foreach (var dataset in ChartDatasets.Where(id => DatasetsOf(run, tier).Any(entry => entry.Id == id)))
            {
                var rows = new List<(string Label, int Colour, ResultRow Row)>();

                foreach (var (configuration, index) in members.Select((configuration, index) => (configuration, index)))
                {
                    foreach (var (method, label) in ChartWorkloads)
                    {
                        var row = run.Cells.FirstOrDefault(cell =>
                            cell.Name == configuration.Name && cell.Dataset == dataset
                            && cell.Workload.StartsWith(BufferedSuite + "|" + method, StringComparison.Ordinal))?.Row;

                        if (row is { State: "Supported" })
                        {
                            rows.Add(($"{configuration.Name} · {label}", index, row));
                        }
                    }
                }

                Emit(folder, charts, $"market-{tier}-{dataset}", $"{tier} · {dataset}", rows);
            }
        }

        if (protectedRun is not null)
        {
            foreach (var compressed in new[] { true, false })
            {
                var group = protectedRun.Configurations.Where(configuration => configuration.Compressed == compressed).ToList();
                var suffix = compressed ? "brotli" : "plain";

                foreach (var dataset in ChartDatasets.Where(protectedRun.Datasets.Contains))
                {
                    var rows = new List<(string Label, int Colour, ResultRow Row)>();

                    foreach (var (configuration, index) in group.Select((configuration, index) => (configuration, index)))
                    {
                        foreach (var (method, label) in ChartWorkloads)
                        {
                            if (protectedRun.Find(configuration.Name, dataset, method) is { State: "Supported" } row)
                            {
                                rows.Add(($"{Short(configuration)} · {label}", index, row));
                            }
                        }
                    }

                    Emit(folder, charts, $"market-T4-{suffix}-{dataset}",
                        $"T4 · {dataset} · {(compressed ? "Brotli + CRC-32 + AES-256-GCM" : "CRC-32 + AES-256-GCM")}", rows);
                }
            }
        }

        return charts;
    }

    /// <summary>A composed baseline's name without the phases its chart already states.</summary>
    private static string Short(Protection configuration) =>
        configuration.Library == Viper
            ? $"{Viper} V1"
            : $"{configuration.Library} composed";

    /// <summary>Two charts for one table: time with its interval, and allocation.</summary>
    private static void Emit(string folder, List<Chart> charts, string stem, string scope, IReadOnlyList<(string Label, int Colour, ResultRow Row)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        double labelWidth = Math.Min(460, 16 + (rows.Max(row => row.Label.Length) * 6.2));

        var (scale, unit) = TimeUnit(rows.Max(row => row.Row.MeanNs + row.Row.MarginNs));
        var time = new BarChart($"{scope} — time per operation", unit, lowerIsBetter: true, labelWidth);

        foreach (var (label, colour, row) in rows)
        {
            time.Add(label, row.MeanNs / scale, colour, row.MarginNs / scale);
        }

        File.WriteAllText(Path.Combine(folder, $"{stem}-time.svg"), time.Render());
        charts.Add(new Chart($"{stem}-time.svg", $"{scope}, time per operation", $"{unit}, mean with its 99.9 % interval; bars in roster order."));

        var (bytesScale, bytesUnit) = ByteUnit(rows.Max(row => row.Row.AllocatedBytes));
        var allocation = new BarChart($"{scope} — allocated per operation", bytesUnit, lowerIsBetter: true, labelWidth);

        foreach (var (label, colour, row) in rows)
        {
            allocation.Add(label, row.AllocatedBytes / bytesScale, colour);
        }

        File.WriteAllText(Path.Combine(folder, $"{stem}-allocation.svg"), allocation.Render());
        charts.Add(new Chart($"{stem}-allocation.svg", $"{scope}, allocation per operation", $"{bytesUnit} allocated per operation, from MemoryDiagnoser."));
    }

    private static (double Scale, string Unit) TimeUnit(double nanoseconds) => nanoseconds switch
    {
        >= 1_000_000 => (1_000_000, "milliseconds per operation"),
        >= 1_000 => (1_000, "microseconds per operation"),
        _ => (1, "nanoseconds per operation"),
    };

    private static (double Scale, string Unit) ByteUnit(double bytes) => bytes switch
    {
        >= 1_048_576 => (1_048_576, "MB"),
        >= 1_024 => (1_024, "KB"),
        _ => (1, "bytes"),
    };

    private static void Gallery(Document document, IReadOnlyList<Chart> charts)
    {
        if (charts.Count == 0)
        {
            return;
        }

        document.Heading(2, "Charts");
        document.Paragraph(
            "Generated from this run's raw files and from nothing else, for the ordinary object (DATA-02), a small " +
            "batch (DATA-03) and a large one (DATA-04). Each chart holds one tier's members — or, for T4, the " +
            "configurations that do the same work — in roster order; whiskers that overlap are no measurable " +
            "difference. The tables above hold every dataset and the ratios.");

        foreach (var chart in charts)
        {
            document.Image($"charts/{chart.File}", $"{chart.Title} — {chart.Note}");
        }
    }
}
