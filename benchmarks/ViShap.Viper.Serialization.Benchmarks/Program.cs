using BenchmarkDotNet.Running;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Environment;
using ViShap.Viper.Serialization.Benchmarks.Reporting;
using ViShap.Viper.Serialization.Benchmarks.Suites;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--verify", StringComparer.Ordinal))
        {
            return Verify();
        }

        if (args.Contains("--manifest", StringComparer.Ordinal))
        {
            return Manifest();
        }

        if (args.Contains("--sizes", StringComparer.Ordinal))
        {
            return Sizes();
        }

        if (args is ["--cold-child", var profile, var dataset, var operation, ..])
        {
            return ColdStartRunner.Child(profile, dataset, operation, args.Length > 4 ? args[4] : null);
        }

        // A track run carries the same flags the single modes use — --filter, --soak — so it is
        // recognized before any of them.
        if (args.Contains("--track", StringComparer.Ordinal))
        {
            return Track(args);
        }

        if (args.Contains("--report", StringComparer.Ordinal))
        {
            return Report(args);
        }

        if (args.Contains("--cold", StringComparer.Ordinal))
        {
            return ColdStartRunner.Drive();
        }

        if (args.Contains("--contract-cold", StringComparer.Ordinal))
        {
            return Suites.Components.ContractColdRunner.Run();
        }

        if (args.Contains("--soak", StringComparer.Ordinal))
        {
            var minutes = Minutes(args, fallback: 10);
            return SoakRunner.Run(TimeSpan.FromMinutes(minutes));
        }

        if (args.Contains("--smoke", StringComparer.Ordinal))
        {
            return Smoke(args);
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new BenchmarkConfig());
        return 0;
    }

    /// <summary>
    /// §29.1 — one command runs a whole track: every suite in order, every artifact of §23 into the
    /// run's own directory, and nothing interactive in the middle.
    /// </summary>
    private static int Track(string[] args)
    {
        var track = Argument(args, "--track");

        if (!string.Equals(track, "A", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"Track '{track}' has no runner. Track A measures Viper alone; Track B follows it and needs " +
                "the competitor roster of the plan's §5.");

            return 1;
        }

        return TrackRunner.Run(args);
    }

    /// <summary>
    /// REP-02 — regenerates the report and the charts of an existing run from its raw files, so a
    /// baseline committed long ago still produces the report it produced on the day it was taken.
    /// </summary>
    private static int Report(string[] args)
    {
        var directory = Argument(args, "--report");

        if (directory is null || !Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No run directory at '{directory}'.");
            return 1;
        }

        ReportWriter.Generate(directory);

        Console.WriteLine($"Report and charts regenerated in {directory}");
        return 0;
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// LAY-04 — runs every suite once, to prove the switcher still executes. The numbers a smoke run
    /// produces are meaningless by construction and are never published.
    /// </summary>
    /// <remarks>
    /// A <c>--filter</c> given after <c>--smoke</c> narrows it; without one it runs the whole assembly.
    /// </remarks>
    private static int Smoke(string[] args)
    {
        var filters = args.Contains("--filter", StringComparer.Ordinal)
            ? args.Where(argument => argument != "--smoke").ToArray()
            : ["--filter", "*"];

        var summaries = BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(filters, new SmokeConfig())
            .ToList();

        var failed = summaries
            .SelectMany(summary => summary.Reports)
            .Where(report => !report.Success)
            .ToList();

        Console.WriteLine();
        Console.WriteLine(
            $"Smoke: {summaries.Sum(summary => summary.Reports.Length)} benchmarks, {failed.Count} failed.");

        foreach (var report in failed)
        {
            Console.WriteLine($"  failed: {report.BenchmarkCase.DisplayInfo}");
        }

        return failed.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Proves every (adapter, dataset) pair round-trips before any suite is allowed to time it, and
    /// records the payload sizes the same serialization produced.
    /// </summary>
    private static int Verify()
    {
        var results = RoundTripVerifier.VerifyAll();

        Console.WriteLine($"{"Adapter",-22} {"Dataset",-22} {"State",-12} {"Buffered",10} {"Streamed",10}  Detail");
        Console.WriteLine(new string('-', 100));

        foreach (var result in results)
        {
            Console.WriteLine(
                $"{result.Adapter,-22} {result.Dataset,-22} {result.State,-12} " +
                $"{result.BufferedBytes,10} {result.StreamedBytes,10}  {result.Detail}");
        }

        var output = Path.Combine(AppContext.BaseDirectory, "verification.csv");
        RoundTripVerifier.Write(results, output);

        var failed = results.Count(result => result.State == VerificationState.Failed);

        Console.WriteLine();
        Console.WriteLine($"{results.Count} pairs, {failed} failed. Written to {output}");
        Console.WriteLine($"Corpus: {Corpus.All.Count} datasets.");

        return failed == 0 ? 0 : 1;
    }

    /// <summary>§14 — sizes, recorded without a timing in the same table.</summary>
    private static int Sizes()
    {
        var rows = Reporting.SizeReport.Collect();

        Directory.CreateDirectory(Environment.Paths.Artifacts);
        var output = Path.Combine(Environment.Paths.Artifacts, "payload-sizes.csv");
        Reporting.SizeReport.Write(rows, output);

        Console.WriteLine($"{"Profile",-8} {"Dataset",-22} {"Bytes",12} {"Envelope",10} {"Refs",8}  Ratio");
        Console.WriteLine(new string('-', 78));

        foreach (var row in rows)
        {
            Console.WriteLine(
                $"{row.Profile,-8} {row.Dataset,-22} {row.Bytes,12} {row.EnvelopeBytes,10} " +
                $"{row.ReferenceFramingBytes,8}  {row.CompressionRatio:F3}");
        }

        Console.WriteLine();
        Console.WriteLine($"{rows.Count} rows. Written to {output}");

        return 0;
    }

    private static double Minutes(string[] args, double fallback)
    {
        var index = Array.IndexOf(args, "--soak");

        return index >= 0 && index + 1 < args.Length
            && double.TryParse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture, out var minutes)
                ? minutes
                : fallback;
    }

    private static int Manifest()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "environment.json");
        EnvironmentManifest.Write(output);

        Console.WriteLine(File.ReadAllText(output));
        Console.WriteLine($"Written to {output}");

        return 0;
    }
}
