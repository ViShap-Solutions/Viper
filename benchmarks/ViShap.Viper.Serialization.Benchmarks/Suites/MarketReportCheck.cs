using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters.Market;
using ViShap.Viper.Serialization.Benchmarks.Reporting;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// The harness's own check of the Track B report: <c>--check-market-report</c>. It writes a run
/// directory outside the repository from this harness's verification and a cell for every case every
/// comparative suite would time — numbers made up, so the directory is deleted afterwards and nothing of
/// it can be read as a measurement — generates the report twice, and holds it to the plan's rules:
/// tiers, roster and exclusions before any number (REP-05, RST-06), every rationale (FAIR-13), every
/// model difference beside its datasets (FAIR-20), every comparative table naming its tier and holding
/// only the tier's members (TIER-01, TIER-02), compressed configurations in their own section only
/// (FAIR-16), and no blank cell (REP-04).
/// </summary>
internal static class MarketReportCheck
{
    private static readonly List<string> Failures = [];

    internal static int Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"viper-market-report-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            Console.WriteLine("── the verification of the market");
            var verification = MarketVerifier.VerifyAll();
            MarketVerifier.Write(verification, directory);
            MarketArtifacts.Write(directory);
            MarketCases.Record(verification);

            var rows = Cells();
            ResultRow.Write(rows, Path.Combine(directory, "results.csv"));
            Record().Write(Path.Combine(directory, "run.json"));

            Console.WriteLine($"── the report over {rows.Count} made-up cells");
            var drifted = ReportWriter.GenerateAndCheck(directory);
            Check(drifted.Count == 0, $"the report differs between two generations: {string.Join(", ", drifted)}");

            Hold(File.ReadAllLines(Path.Combine(directory, "report.md")), verification);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine();

        foreach (var failure in Failures)
        {
            Console.WriteLine($"  FAILED  {failure}");
        }

        Console.WriteLine(Failures.Count == 0 ? "Market report check: every check holds." : $"Market report check: {Failures.Count} failed.");
        return Failures.Count == 0 ? 0 : 1;
    }

    /// <summary>One cell per case per workload, as each comparative suite would produce it.</summary>
    private static List<ResultRow> Cells()
    {
        var rows = new List<ResultRow>();
        var seed = 0;

        foreach (var suite in new[]
                 {
                     typeof(MarketBufferedBenchmarks), typeof(MarketStreamBenchmarks), typeof(MarketBufferBenchmarks),
                     typeof(MarketSpanBenchmarks), typeof(MarketEvolutionBenchmarks),
                 })
        {
            var cases = (IEnumerable<MarketCase>)suite.GetProperty("Cases", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            var methods = suite.GetMethods().Select(method => method.GetCustomAttribute<BenchmarkAttribute>()).OfType<BenchmarkAttribute>().ToList();

            foreach (var @case in cases)
            {
                foreach (var method in methods)
                {
                    double mean = 1_000 + (seed++ % 97) * 37.5;
                    rows.Add(new ResultRow(
                        suite.FullName!, $"'{method.Description}'", $"Case={@case}", "Check", "Supported",
                        mean, mean * 0.01, mean * 0.03, mean, mean * 1.05, 1e9 / mean, 400, 0.1, 0, 0, 15));
                }
            }
        }

        return rows;
    }

    private static RunRecord Record() => new(
        Kind: "Measurement",
        Label: "market-report-check",
        Tag: null,
        Describe: "market-report-check",
        Revision: "check",
        Dirty: false,
        AllowDirty: false,
        Reason: "made-up cells for the harness's check of the report",
        Track: MarketReport.Track,
        Job: "Check",
        Filter: null,
        StartedUtc: "2026-01-01T00:00:00.0000000Z",
        FinishedUtc: "2026-01-01T00:00:00.0000000Z",
        Commands: ["--check-market-report"],
        SuitesRun: [],
        SuitesNotRun: []);

    private static void Hold(string[] report, MarketVerification verification)
    {
        int Index(string prefix) => Array.FindIndex(report, line => line.StartsWith(prefix, StringComparison.Ordinal));

        int firstNumber = Index("## Comparative results");
        Check(firstNumber > 0, "the report has no comparative section");

        foreach (var heading in new[] { "## Capability tiers", "## Roster", "### Second circle", "### Excluded" })
        {
            int at = Index(heading);
            Check(at > 0 && at < firstNumber, $"REP-05: '{heading}' is not before the first number");
        }

        foreach (var library in MarketExclusions.Excluded.Concat(MarketExclusions.SecondCircle))
        {
            Check(report.Any(line => line.StartsWith($"| {library.Library} |", StringComparison.Ordinal)), $"RST-06: {library.Library} is not in the report");
        }

        foreach (var adapter in MarketRoster.All)
        {
            Check(report.Contains($"### {adapter.Library} {adapter.Version} — {adapter.Configuration}"), $"FAIR-13: no rationale for {adapter.Name}");
        }

        foreach (var difference in ModelFamiliesDifferences())
        {
            Check(report.Any(line => line.StartsWith("- Model difference:", StringComparison.Ordinal) && line.Contains(difference, StringComparison.Ordinal)),
                $"FAIR-20: the difference on {difference} is under no dataset's table");
        }

        var members = verification.Tiers
            .Where(placement => placement is { Intended: true, Qualified: true })
            .Select(placement => (Tier: placement.Tier.ToString(), placement.Adapter.Name))
            .ToHashSet();

        var compressed = MarketRoster.All.Where(adapter => adapter.Compressed).Select(adapter => adapter.Name).ToHashSet(StringComparer.Ordinal);

        string? table = null;
        int tables = 0, cells = 0;

        for (var index = 0; index < report.Length; index++)
        {
            var line = report[index];

            if (line.StartsWith("#### ", StringComparison.Ordinal))
            {
                table = line[5..];
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                table = null;
            }

            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                continue;
            }

            var cellsOf = Split(line);
            Check(cellsOf.All(cell => cell.Length > 0), $"REP-04: a blank cell in '{line}'");

            if (table is null)
            {
                continue;
            }

            if (cellsOf[0] == "Tier")
            {
                tables++;
                continue;
            }

            if (cellsOf[0] == "---" || line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            var name = cellsOf[1].Replace(" (Partial)", string.Empty, StringComparison.Ordinal);
            cells += cellsOf.Count - 2;

            if (table.StartsWith("Compressed", StringComparison.Ordinal))
            {
                Check(compressed.Contains(name), $"FAIR-16: {name} is in the compressed section and does not compress");
                continue;
            }

            var tier = table[..2];
            Check(cellsOf[0] == tier, $"TIER-02: a row of '{table}' says tier {cellsOf[0]}");
            Check(members.Contains((tier, name)), $"TIER-01: {name} is in '{table}' and the probes did not place it in {tier}");
            Check(!compressed.Contains(name), $"FAIR-16: the compressed {name} is in '{table}'");
        }

        Check(tables > 0, "no comparative table was written");
        Check(report.Any(line => line.StartsWith("#### Compressed", StringComparison.Ordinal)) || compressed.Count == 0, "FAIR-16: no compressed section");

        foreach (var line in report.Where(line => line.StartsWith("| ", StringComparison.Ordinal)))
        {
            Check(!line.Contains("not measured in this run", StringComparison.Ordinal), $"a case every suite would time is reported as not measured: '{line}'");
        }

        Console.WriteLine(
            $"   {tables} comparative tables, {cells.ToString(CultureInfo.InvariantCulture)} cells, " +
            $"{members.Count} tier memberships, {compressed.Count} compressed configurations");
    }

    /// <summary>Each structural difference as the report's note names it: the type with what changed.</summary>
    private static IEnumerable<string> ModelFamiliesDifferences() =>
        Models.ModelFamilies.Differences.Select(difference => $"{difference.Type} {difference.Member} {difference.Difference}");

    private static List<string> Split(string line) =>
    [
        .. line.Trim().Trim('|').Split(" | ").Select(cell => cell.Trim()),
    ];

    private static void Check(bool holds, string failure)
    {
        if (!holds)
        {
            Failures.Add(failure);
        }
    }
}
