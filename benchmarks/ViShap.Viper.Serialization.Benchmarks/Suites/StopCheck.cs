using System.Diagnostics;
using System.Reflection;
using ViShap.Viper.Serialization.Benchmarks.Environment;
using ViShap.Viper.Serialization.Benchmarks.Reporting;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// The harness's own check of how a track run ends early: <c>--check-stop</c>. It proves that the cells
/// recovered from BenchmarkDotNet's exports are the cells a run writes, that a run stopped by a
/// <c>STOP</c> file after its second suite writes the record of those two suites, that a run killed in
/// its third suite is turned into the same record by <c>--report</c>, and that neither is ever a
/// baseline.
/// </summary>
/// <remarks>
/// The two runs it starts are shortened runs of three small suites, so they cost minutes and their
/// numbers mean nothing; their directories are deleted afterwards unless <c>--keep</c> is given.
/// <c>--offline</c> runs only the comparison over the committed runs, which starts no process.
/// </remarks>
internal static class StopCheck
{
    private static readonly string[] Suites = ["BudgetBenchmarks", "ReferenceScopeBenchmarks", "HeaderBenchmarks"];

    private static readonly string[] Steps = ["member-plan construction", "first use", "cold start"];

    private static readonly List<string> Failures = [];

    internal static int Run(string[] args)
    {
        Recovered();
        NeverBaseline();

        if (!args.Contains("--offline", StringComparer.Ordinal))
        {
            bool keep = args.Contains("--keep", StringComparer.Ordinal);
            var stopped = Stopped();
            var killed = Killed();

            if (stopped is not null && killed is not null)
            {
                Agree(stopped, killed);
            }

            foreach (var directory in new[] { stopped, killed }.OfType<string>().Where(_ => !keep))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        Console.WriteLine();

        foreach (var failure in Failures)
        {
            Console.WriteLine($"  FAILED  {failure}");
        }

        Console.WriteLine(Failures.Count == 0 ? "Stop check: every check holds." : $"Stop check: {Failures.Count} failed.");
        return Failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Every committed run that kept its export: the cells rebuilt from the export equal the cells of
    /// its <c>results.csv</c>, value for value. A run older than the separate Server GC export lost the
    /// first pass of that suite to the second, and only those cells may be missing.
    /// </summary>
    private static void Recovered()
    {
        Console.WriteLine("── recovery against the committed runs");

        var order = TrackRunner.SuiteOrder;

        foreach (var directory in Directory.EnumerateDirectories(Paths.Measurements)
                     .Concat(Directory.EnumerateDirectories(Paths.Baselines))
                     .Order(StringComparer.Ordinal))
        {
            var csv = Path.Combine(directory, "results.csv");

            if (!File.Exists(csv) || !File.Exists(Path.Combine(directory, "results.zip")))
            {
                continue;
            }

            var committed = ResultRow.Read(csv);
            var recovered = ExportRecovery.Read(directory, order).SelectMany(export => export.Rows).ToList();
            var expected = committed.ToHashSet();

            var foreign = recovered.Where(row => !expected.Contains(row)).ToList();
            var missing = committed.Except(recovered).ToList();

            // The first pass of a suite measured twice was overwritten by the second in an export written
            // before the passes had separate directories; nothing else may be absent.
            var overwritten = committed
                .Where(row => row.Job.EndsWith("ServerGc", StringComparison.Ordinal))
                .Select(row => row.Suite)
                .ToHashSet(StringComparer.Ordinal);

            var unexplained = missing.Where(row => !overwritten.Contains(row.Suite) || row.Job.EndsWith("ServerGc", StringComparison.Ordinal)).ToList();

            var name = Path.GetFileName(directory);
            Console.WriteLine(
                $"   {name,-48} {committed.Count,5} committed  {recovered.Count,5} recovered" +
                (missing.Count > unexplained.Count ? $"  ({missing.Count - unexplained.Count} lost to the Server GC pass)" : string.Empty));

            Check(foreign.Count == 0, $"{name}: {foreign.Count} recovered cell(s) differ from results.csv, first {foreign.FirstOrDefault()}");
            Check(unexplained.Count == 0, $"{name}: {unexplained.Count} cell(s) of results.csv are not in the export, first {unexplained.FirstOrDefault()}");
        }
    }

    /// <summary>An interrupted run is a measurement under any tag, and a baseline is never rebuilt.</summary>
    private static void NeverBaseline()
    {
        Console.WriteLine("── an interrupted run is never a baseline");

        var working = Path.Combine(Paths.Measurements, "v9.9.9-0-gcheck-20000101T000000Z");
        var baseline = new RunTarget(
            RunKind.Baseline,
            Path.Combine(Paths.Baselines, "v9.9.9"),
            working,
            "v9.9.9",
            "v9.9.9",
            "v9.9.9-0-gcheck",
            "every suite of the track, on a tagged commit, under the publication job",
            Dirty: false,
            AllowDirty: false);

        var interrupted = baseline.Interrupted();

        Check(interrupted.Kind == RunKind.Measurement, "a baseline target interrupted is still a baseline");
        Check(interrupted.Directory == working, "an interrupted baseline target still writes under Baselines/");
        Check(interrupted.File("run.json").StartsWith(Paths.Measurements, StringComparison.Ordinal), "an interrupted baseline target writes outside Measurements/");
        Check(baseline.File("run.json").StartsWith(Paths.Measurements, StringComparison.Ordinal), "a baseline is measured outside Measurements/ before it is complete");

        Console.WriteLine("   --report on a directory under Baselines/ with no results.csv:");
        Check(TrackRunner.Recover(Path.Combine(Paths.Baselines, "v9.9.9-never")) == 1, "--report rebuilt a baseline from its exports");
    }

    /// <summary>A run of three suites, stopped by a STOP file while its second suite runs.</summary>
    private static string? Stopped()
    {
        Console.WriteLine("── a run stopped by a STOP file during its second suite");

        var (directory, exit) = Track(line =>
        {
            if (line.StartsWith("── [2/3]", StringComparison.Ordinal))
            {
                return Act.Stop;
            }

            return Act.None;
        });

        if (directory is null)
        {
            return null;
        }

        Check(exit == 2, $"the stopped run exited with {exit}, not 2");
        Check(!File.Exists(Path.Combine(directory, "STOP")), "the STOP file was left behind");
        Expect(directory, stoppedBy: "the file STOP", unfinished: []);

        return directory;
    }

    /// <summary>A run of three suites, killed with its process tree in its third suite, then <c>--report</c>.</summary>
    private static string? Killed()
    {
        Console.WriteLine("── a run killed during its third suite, then rebuilt by --report");

        var (directory, exit) = Track(line => line.StartsWith("── [3/3]", StringComparison.Ordinal) ? Act.Kill : Act.None);

        if (directory is null)
        {
            return null;
        }

        Check(!File.Exists(Path.Combine(directory, "results.csv")), "the killed run wrote results.csv");

        var kept = RunRecord.Read(Path.Combine(directory, "run.json"));
        Check(kept.Interrupted && kept.Kind == nameof(RunKind.Measurement), "the record a killed run leaves does not read as an interrupted measurement");

        Console.WriteLine($"   killed (exit {exit}); --report:");
        int rebuilt = Launch(["--report", directory], _ => Act.None, out _);

        Check(rebuilt == 2, $"--report on the killed run exited with {rebuilt}, not 2");
        Expect(directory, stoppedBy: "the end of the process, before the run finished", unfinished: [Suites[2]]);

        return directory;
    }

    /// <summary>What both runs must hold: two suites measured, the rest named as not run, a measurement.</summary>
    private static void Expect(string directory, string stoppedBy, IReadOnlyList<string> unfinished)
    {
        var name = Path.GetFileName(directory);
        var record = RunRecord.Read(Path.Combine(directory, "run.json"));
        var rows = ResultRow.Read(Path.Combine(directory, "results.csv"));

        Check(record.Kind == nameof(RunKind.Measurement), $"{name}: recorded as {record.Kind}");
        Check(directory.StartsWith(Paths.Measurements, StringComparison.Ordinal), $"{name}: written outside Measurements/");
        Check(record.Interrupted, $"{name}: not marked interrupted");
        Check(record.StoppedBy == stoppedBy, $"{name}: stopped by '{record.StoppedBy}', expected '{stoppedBy}'");
        Check(record.SuitesRun.SequenceEqual(Suites[..2]), $"{name}: suites run [{string.Join(", ", record.SuitesRun)}]");
        Check((record.SuitesUnfinished ?? []).SequenceEqual(unfinished), $"{name}: unfinished [{string.Join(", ", record.SuitesUnfinished ?? [])}]");

        var notRun = unfinished.Count == 0 ? Suites[2..].Concat(Steps) : Steps;
        Check(notRun.All(record.SuitesNotRun.Contains), $"{name}: not run [{string.Join(", ", record.SuitesNotRun)}] misses one of [{string.Join(", ", notRun)}]");

        var suites = rows.Select(row => row.SuiteName).Distinct().ToList();
        Check(suites.SequenceEqual(Suites[..2]), $"{name}: results.csv holds [{string.Join(", ", suites)}]");
        Check(rows.Count > 0 && rows.All(row => row.State == "Supported"), $"{name}: a cell of results.csv has no number");

        foreach (var file in new[] { "memory.csv", "components.csv", "manifest.md", "scope.md", "reproduction.md", "report.md", "report.html", "results.zip" })
        {
            Check(File.Exists(Path.Combine(directory, file)), $"{name}: {file} was not written");
        }

        Check(!Directory.Exists(Path.Combine(directory, "results")), $"{name}: the loose export was not archived");
        Check(File.ReadAllText(Path.Combine(directory, "scope.md")).Contains("## Interrupted", StringComparison.Ordinal), $"{name}: scope.md does not say the run was interrupted");
        Check(File.ReadAllText(Path.Combine(directory, "report.md")).Contains("**Interrupted**", StringComparison.Ordinal), $"{name}: report.md does not say the run was interrupted");

        Console.WriteLine($"   {name}: {rows.Count} cells of {suites.Count} suites, {record.SuitesNotRun.Count} not run");
    }

    /// <summary>The stopped run and the rebuilt one measured the same cells, whatever their numbers.</summary>
    private static void Agree(string stopped, string killed)
    {
        static List<string> Keys(string directory) =>
            [.. ResultRow.Read(Path.Combine(directory, "results.csv")).Select(row => $"{row.Suite}|{row.Method}|{row.Parameters}|{row.Job}")];

        Check(Keys(stopped).SequenceEqual(Keys(killed)), "the stopped run and the rebuilt one hold different cells");
    }

    private enum Act
    {
        None,
        Stop,
        Kill,
    }

    private static (string? Directory, int Exit) Track(Func<string, Act> watch)
    {
        int exit = Launch(["--track", "A", "--short", "--filter", string.Join(",", Suites)], watch, out var directory);

        Check(directory is not null, "the track run did not announce its directory");
        return (directory, exit);
    }

    /// <summary>
    /// Runs this harness in a child process with standard input redirected — so Ctrl+C is not read and a
    /// STOP file is the way to stop — and acts on its output line by line.
    /// </summary>
    private static int Launch(IReadOnlyList<string> arguments, Func<string, Act> watch, out string? directory)
    {
        var executable = System.Environment.ProcessPath ?? "dotnet";
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.ErrorDataReceived += (_, line) => { if (line.Data is { Length: > 0 }) Console.WriteLine($"   ! {line.Data}"); };
        process.BeginErrorReadLine();

        string? found = null;
        string? line;

        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            if (line.StartsWith("  destination  ", StringComparison.Ordinal))
            {
                found = line["  destination  ".Length..].Trim();
            }

            if (line.StartsWith("──", StringComparison.Ordinal) || line.StartsWith("Measurement", StringComparison.Ordinal))
            {
                Console.WriteLine($"   | {line}");
            }

            switch (watch(line))
            {
                case Act.Stop when found is not null:
                    File.WriteAllText(Path.Combine(found, "STOP"), string.Empty);
                    Console.WriteLine("   > STOP written");
                    break;

                case Act.Kill:
                    // Long enough for BenchmarkDotNet to be inside the suite, so the kill cuts it off.
                    Thread.Sleep(TimeSpan.FromSeconds(15));
                    process.Kill(entireProcessTree: true);
                    Console.WriteLine("   > process tree killed");
                    break;
            }
        }

        process.WaitForExit();
        directory = found;
        return process.ExitCode;
    }

    private static void Check(bool holds, string failure)
    {
        if (!holds)
        {
            Failures.Add(failure);
        }
    }
}
