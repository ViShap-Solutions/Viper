using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Environment;
using ViShap.Viper.Serialization.Benchmarks.Reporting;
using ViShap.Viper.Serialization.Benchmarks.Suites.Components;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// The one command of Benchmark-Plan §29.1: it runs every suite of Track A in order, writes every
/// artifact of §23 into the run's own directory, and stops. There is no interactive step and no decision
/// in the middle.
/// </summary>
/// <remarks>
/// Where it writes is decided by git and by the shape of the run, never by a flag that names a version
/// (BASE-08). A tagged commit measured in full under the publication job produces a baseline; anything
/// else produces a partial run under `Measurements/`, which says in `scope.md` what it did not measure.
/// </remarks>
internal static class TrackRunner
{
    /// <summary>
    /// Every timed suite of Track A, in the order the stages of §29.1 work them. The order is part of
    /// the record: a report reads the same sequence every time.
    /// </summary>
    private static readonly (string Stage, Type Suite)[] TrackA =
    [
        // A1 — the profile matrix, and the floor every cell is read against.
        ("A1", typeof(ProfileMatrixBenchmarks)),
        ("A1", typeof(ProfileStreamBenchmarks)),
        ("A1", typeof(HarnessFloorBenchmarks)),

        // A3 — the algorithm phases, and the envelope by difference.
        ("A3", typeof(AlgorithmBenchmarks)),
        ("A3", typeof(EnvelopeDifferentialBenchmarks)),

        // A4 — the component record.
        ("A4", typeof(ValuePrimitiveBenchmarks)),
        ("A4", typeof(ValueTextBenchmarks)),
        ("A4", typeof(BudgetBenchmarks)),
        ("A4", typeof(DepthScopeBenchmarks)),
        ("A4", typeof(ContractLookupBenchmarks)),
        ("A4", typeof(FormatterResolutionBenchmarks)),
        ("A4", typeof(FormatterShapeBenchmarks)),
        ("A4", typeof(ReferenceIdentityBenchmarks)),
        ("A4", typeof(ReferenceScopeBenchmarks)),
        ("A4", typeof(HeaderBenchmarks)),
        ("A4", typeof(StreamMechanismBenchmarks)),
        ("A4", typeof(CompressionPrimitiveBenchmarks)),
        ("A4", typeof(ProtectionPrimitiveBenchmarks)),

        // A5 — the scaling curves.
        ("A5", typeof(ElementCountScalingBenchmarks)),
        ("A5", typeof(PayloadSizeScalingBenchmarks)),
        ("A5", typeof(DepthScalingBenchmarks)),
        ("A5", typeof(StringScalingBenchmarks)),
        ("A5", typeof(DictionaryScalingBenchmarks)),
        ("A5", typeof(SharingDensityBenchmarks)),
        ("A5", typeof(MemberCountScalingBenchmarks)),

        // A6 — concurrency. Cold start and the soak run are process-level and follow the suites.
        ("A6", typeof(ConcurrencyBenchmarks)),
    ];

    internal static int Run(string[] args)
    {
        bool shortened = args.Contains("--short", StringComparer.Ordinal);
        bool allowDirty = args.Contains("--allow-dirty", StringComparer.Ordinal);
        string? filter = Value(args, "--filter");
        double soakMinutes = Number(args, "--soak", shortened ? 0 : 10);

        var unregistered = Unregistered();

        if (unregistered.Count > 0)
        {
            Console.Error.WriteLine(
                "These suites exist in the assembly but are not registered in the track order, so a run " +
                "would silently leave them out:");

            foreach (var name in unregistered)
            {
                Console.Error.WriteLine($"  {name}");
            }

            return 1;
        }

        var target = RunTarget.Decide(shortened, filter, allowDirty);
        var selected = Select(filter);

        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"No suite of Track A matches '{filter}'. Nothing was written.");
            return 1;
        }

        if (args.Contains("--list", StringComparer.Ordinal))
        {
            Console.WriteLine($"{selected.Count} of {TrackA.Length} suite(s) would run:");

            foreach (var entry in TrackA.Where(entry => selected.Contains(entry.Suite)))
            {
                Console.WriteLine($"  {entry.Stage}  {entry.Suite.Name}");
            }

            Console.WriteLine();
            Console.WriteLine($"Destination would be {target.Directory}");
            Console.WriteLine($"Kind would be {target.Kind} — {target.Reason}");

            // A listing that hid the refusal would send the operator off for three hours of measuring
            // that never starts.
            if (target.IsBaseline && target.Dirty && !target.AllowDirty)
            {
                Console.WriteLine(
                    "It would refuse: the working tree has uncommitted changes, so the baseline would not " +
                    "measure what its tag points at.");
            }

            Console.WriteLine("Nothing was measured and nothing was written.");

            return 0;
        }

        try
        {
            target.Create();
        }
        catch (InvalidOperationException refusal)
        {
            Console.Error.WriteLine(refusal.Message);
            return 1;
        }

        var startedUtc = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();

        // One command produced this run. Listing every stage as if it had been run separately would
        // describe a session nobody had; the stage equivalents belong in reproduction.md, offered as an
        // alternative rather than as history.
        var command = $"dotnet run --project {Paths.ProjectName} -c Release -- --track A"
                      + (shortened ? " --short" : string.Empty)
                      + (filter is null ? string.Empty : $" --filter '{filter}'")
                      + (soakMinutes is > 0 and not 10 ? $" --soak {soakMinutes:F0}" : string.Empty)
                      + (allowDirty ? " --allow-dirty" : string.Empty);

        Announce(target, selected, shortened, filter, soakMinutes);

        // §4 — the manifest first, so even a run that fails later says what it ran on.
        Step("manifest", () => EnvironmentManifest.Write(target.File("environment.json")));

        // §7.6 — verification before any timing. A failure here stops the run: a timing over an
        // unverified pair is not a measurement.
        int failedPairs = 0;

        Step("verify", () =>
        {
            var results = RoundTripVerifier.VerifyAll();
            RoundTripVerifier.Write(results, target.File("verification.csv"));
            failedPairs = results.Count(result => result.State == VerificationState.Failed);

            Console.WriteLine($"{results.Count} pairs, {failedPairs} failed.");
        });

        if (failedPairs > 0)
        {
            Console.Error.WriteLine(
                $"{failedPairs} pair(s) did not round-trip. No timing was taken: correctness comes first.");

            return 1;
        }

        // §14 — sizes, which carry no timing beside them.
        Step("sizes", () => SizeReport.Write(SizeReport.Collect(), target.File("payload-sizes.csv")));

        // §10, §18, §19, §21 — the timed suites, in track order, into the run's own directory.
        var summaries = new List<Summary>();

        for (var index = 0; index < selected.Count; index++)
        {
            var suite = selected[index];

            Console.WriteLine();
            Console.WriteLine($"── [{index + 1}/{selected.Count}] {suite.Name}");

            summaries.Add(BenchmarkRunner.Run(
                suite,
                new BenchmarkConfig(target.Directory, shortened)));
        }

        // §19 SCALE-09 — the large end of the payload curve again, under Server GC, so both are published
        // and a reader can see what the GC mode is worth. It is the same suite and the same corpus; only
        // the job differs, and the job id travels with every cell.
        if (selected.Contains(typeof(PayloadSizeScalingBenchmarks)))
        {
            Console.WriteLine();
            Console.WriteLine("── SCALE-09  PayloadSizeScalingBenchmarks under Server GC");

            summaries.Add(BenchmarkRunner.Run(
                typeof(PayloadSizeScalingBenchmarks),
                new BenchmarkConfig(target.Directory, shortened, serverGc: true)));
        }

        // §18 — the cold half of the member plan, which no repeated-invocation harness can measure.
        Step("member-plan construction", () => ContractColdRunner.Run(target.Directory));

        // §20 — cold start, one process per measurement.
        Step("cold start", () => ColdStartRunner.Drive(target.Directory));

        // §22 — sustained load. Skipped in a shortened run, because ten minutes of soak proves nothing
        // about a pipeline and the shortened run exists to prove the pipeline.
        if (soakMinutes > 0)
        {
            Step($"soak {soakMinutes:F0} min", () =>
                SoakRunner.Run(TimeSpan.FromMinutes(soakMinutes), target.Directory));
        }

        // The BenchmarkDotNet export is 27 MB of per-iteration data for a full run, and nothing reads it
        // back: the report and the charts are built from results.csv. A baseline is immutable, so a diff
        // over those files would never be taken either. Archived, it costs a few megabytes per release
        // instead of tens, and the raw measurements are still there for anyone who wants them.
        Step("archive the BenchmarkDotNet export", () => Archive(target));

        var rows = ResultRow.From(summaries);
        ResultRow.Write(rows, target.File("results.csv"));
        ResultRow.WriteMemory(rows, target.File("memory.csv"));
        ResultRow.Write([.. rows.Where(row => row.IsComponent)], target.File("components.csv"));

        var record = new RunRecord(
            Kind: target.Kind.ToString(),
            Label: target.Label,
            Tag: target.Tag,
            Describe: target.Describe,
            Revision: Shell.Git("rev-parse HEAD"),
            Dirty: target.Dirty,
            AllowDirty: target.AllowDirty,
            Reason: target.Reason,
            Track: "A — Viper alone",
            Job: shortened ? "Shortened" : "Publication",
            Filter: filter,
            StartedUtc: startedUtc.ToString("O", CultureInfo.InvariantCulture),
            FinishedUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Commands: [command],
            SuitesRun: [.. selected.Select(suite => suite.Name)],
            SuitesNotRun:
            [
                .. TrackA.Select(entry => entry.Suite).Except(selected).Select(suite => suite.Name),
            ]);

        record.Write(target.File("run.json"));
        Manifest(target, record, rows, timer.Elapsed);
        Reproduction(target, record);

        if (!target.IsBaseline)
        {
            Scope(target, record);
        }

        // The report is generated twice and compared, so a generator that stopped being a pure view over
        // the raw files says so here (REP-02).
        var drifted = ReportWriter.GenerateAndCheck(target.Directory);

        foreach (var file in drifted)
        {
            Console.Error.WriteLine(
                $"{file} differs between two generations from the same raw files: the report is not a view " +
                "over them. This is a harness defect, not a measurement.");
        }

        int failedCells = rows.Count(row => row.State != "Supported");

        Console.WriteLine();
        Console.WriteLine($"{record.Kind} {target.Label} — {rows.Count} cells, {failedCells} without a number.");
        Console.WriteLine($"Written to {target.Directory}");
        Console.WriteLine($"Elapsed {timer.Elapsed:hh\\:mm\\:ss}");

        if (!target.IsBaseline)
        {
            Console.WriteLine($"Not a baseline: {target.Reason}.");
        }

        return failedCells == 0 && drifted.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Packs the BenchmarkDotNet export into one file beside the results it came from. A run that
    /// produced no export leaves nothing behind.
    /// </summary>
    private static void Archive(RunTarget target)
    {
        var exported = Path.Combine(target.Directory, "results");

        if (!Directory.Exists(exported))
        {
            return;
        }

        var archive = target.File("results.zip");
        File.Delete(archive);
        System.IO.Compression.ZipFile.CreateFromDirectory(
            exported, archive, System.IO.Compression.CompressionLevel.SmallestSize, includeBaseDirectory: false);

        long packed = new FileInfo(archive).Length;
        long loose = new DirectoryInfo(exported).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);

        Directory.Delete(exported, recursive: true);

        Console.WriteLine(
            $"   {loose / 1024:N0} KB of export packed into {packed / 1024:N0} KB, " +
            $"{(loose > 0 ? (double)loose / packed : 0):F1}x");
    }

    private static void Announce(
        RunTarget target, IReadOnlyList<Type> selected, bool shortened, string? filter, double soakMinutes)
    {
        Console.WriteLine($"Track A — {selected.Count} suite(s), job {(shortened ? "Shortened" : "Publication")}");
        Console.WriteLine($"  destination  {target.Directory}");
        Console.WriteLine($"  kind         {target.Kind} — {target.Reason}");
        Console.WriteLine($"  filter       {filter ?? "none"}");
        Console.WriteLine($"  soak         {(soakMinutes > 0 ? $"{soakMinutes:F0} min" : "skipped")}");
        Console.WriteLine($"  working tree {(target.Dirty ? "dirty" : "clean")}"
                          + (target.Dirty && target.AllowDirty ? ", allowed by --allow-dirty" : string.Empty));
    }

    private static void Step(string name, Action work)
    {
        Console.WriteLine();
        Console.WriteLine($"── {name}");

        var timer = Stopwatch.StartNew();
        work();

        Console.WriteLine($"   {name} took {timer.Elapsed:hh\\:mm\\:ss}");
    }

    /// <summary>
    /// The suites this run covers. A filter is a comma-separated list, and a suite is selected when any
    /// token names it: either a stage — <c>a1</c>, <c>a3</c>, <c>a4</c>, <c>a5</c>, <c>a6</c> — or a glob
    /// over the suite's type name. So <c>a4</c> is the whole component record, <c>a1,*Scaling*</c> is the
    /// profile matrix plus every curve, and <c>*Header*,*Budget*</c> is two suites.
    /// </summary>
    private static IReadOnlyList<Type> Select(string? filter)
    {
        if (filter is null)
        {
            return [.. TrackA.Select(entry => entry.Suite)];
        }

        var tokens = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return
        [
            .. TrackA
                .Where(entry => tokens.Any(token =>
                    string.Equals(token, entry.Stage, StringComparison.OrdinalIgnoreCase)
                    || Matches(entry.Suite.Name, token)))
                .Select(entry => entry.Suite),
        ];
    }

    /// <summary>A glob over the suite's type name, which is what one token of `--filter` may be.</summary>
    private static bool Matches(string name, string pattern)
    {
        var parts = pattern.Split('*');
        var position = 0;

        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0)
            {
                continue;
            }

            int found = name.IndexOf(parts[index], position, StringComparison.OrdinalIgnoreCase);

            if (found < 0 || (index == 0 && !pattern.StartsWith('*') && found != 0))
            {
                return false;
            }

            position = found + parts[index].Length;
        }

        return pattern.EndsWith('*') || position == name.Length;
    }

    /// <summary>
    /// Suites that carry benchmarks but are missing from the track order. A run refuses rather than
    /// quietly measuring less than it claims.
    /// </summary>
    private static IReadOnlyList<string> Unregistered() =>
    [
        .. typeof(TrackRunner).Assembly.GetTypes()
            .Where(type => type is { IsPublic: true, IsAbstract: false })
            .Where(type => type.GetMethods().Any(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .Where(type => !TrackA.Any(entry => entry.Suite == type))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal),
    ];

    private static void Manifest(
        RunTarget target, RunRecord record, IReadOnlyList<ResultRow> rows, TimeSpan elapsed)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"# {record.Kind} {record.Label}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"- track: {record.Track}\n");
        text.Append(CultureInfo.InvariantCulture, $"- job: {record.Job}\n");
        text.Append(CultureInfo.InvariantCulture, $"- tag: {record.Tag ?? "none"}\n");
        text.Append(CultureInfo.InvariantCulture, $"- git describe: {record.Describe}\n");
        text.Append(CultureInfo.InvariantCulture, $"- revision: {record.Revision}\n");
        text.Append(CultureInfo.InvariantCulture, $"- started (UTC): {record.StartedUtc}\n");
        text.Append(CultureInfo.InvariantCulture, $"- finished (UTC): {record.FinishedUtc}\n");
        text.Append(CultureInfo.InvariantCulture, $"- elapsed: {elapsed:hh\\:mm\\:ss}\n");
        text.Append(CultureInfo.InvariantCulture, $"- suites run: {record.SuitesRun.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"- timed cells: {rows.Count}\n");
        text.Append(CultureInfo.InvariantCulture,
            $"- cells without a number: {rows.Count(row => row.State != "Supported")}\n");
        text.Append(CultureInfo.InvariantCulture, $"- why this kind: {record.Reason}\n\n");
        text.Append("The machine, the runtime, the GC mode and every pinned package are in `environment.json`.\n");

        File.WriteAllText(target.File("manifest.md"), text.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// §26 — the commands that produced this run, in the order they ran, naming only what the repository
    /// contains, because a reader of this directory has the repository and nothing else.
    /// </summary>
    private static void Reproduction(RunTarget target, RunRecord record)
    {
        var checkout = record.Tag ?? record.Revision;
        var run = $"dotnet run --project {Paths.ProjectName} -c Release --";
        var commands = string.Join(System.Environment.NewLine, record.Commands);
        var folder = record.Kind == nameof(RunKind.Baseline) ? "Baselines" : "Measurements";
        var tagged = record.Tag is null ? string.Empty : $", tag `{record.Tag}`";

        var text =
            $"""
            # Reproducing {record.Label}

            Revision `{record.Revision}`{tagged}. Nothing below depends on a file outside this repository.

            ## Machine state

            The timings of this run belong to the machine `environment.json` records. Reproduce them on the
            same hardware, on mains power, with no other interactive workload, and under the same GC mode.
            Payload sizes, compression ratios and result states are properties of the format and hold on any
            machine.

            ## Commands, in order

            ```text
            git clone <this repository>
            git checkout {checkout}
            dotnet restore Viper.sln
            dotnet build Viper.sln --configuration Release --no-restore
            {commands}
            ```

            The last line is the whole of it: it writes the manifest, verifies every dataset, records the
            sizes, runs the suites in order, measures cold start and sustained load, and generates this
            directory's report and charts.

            ## Re-running one part alone

            ```text
            {run} --verify          # round trips, no timing
            {run} --sizes           # the size table
            {run} --manifest        # the environment
            {run} --contract-cold   # member-plan construction
            {run} --cold            # cold start
            {run} --soak 10         # sustained load
            {run} --track A --filter '*Algorithm*'
            {run} --report {folder}/{record.Label}
            ```

            A filter narrows the run to the suites whose name matches it, and a narrowed run is a partial
            run: it lands under `Measurements/` and never under `Baselines/`. The last line regenerates the
            report and the charts from the files already in this directory, which is what makes the report a
            view over them rather than a record of its own.

            ## Checking these instructions are complete

            Run them in a container that starts from the vendor image and holds nothing from this machine. It
            compares the payload sizes, the verification outcomes and the result states, which are
            machine-independent, and it does not compare timings.

            ```text
            docker run --rm -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0                 bash /src/{Paths.ProjectName}/reproduction/check.sh {checkout}                 {Paths.ProjectName}/{folder}/{record.Label}
            ```

            """;

        File.WriteAllText(target.File("reproduction.md"), text, Encoding.UTF8);
    }

    /// <summary>
    /// What a partial run did not measure. Without it a directory holding half the suites could be read
    /// as a baseline that happens to be small (REP-13).
    /// </summary>
    private static void Scope(RunTarget target, RunRecord record)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"# Scope of {record.Label}\n\n");
        text.Append("**This is not a baseline.** ").Append(record.Reason).Append(".\n\n");
        text.Append("A figure in this directory describes only what the suites below measured, on the revision\n");
        text.Append("`").Append(record.Revision).Append("`. Nothing here may be cited about anything else.\n\n");

        text.Append(CultureInfo.InvariantCulture, $"## Measured — {record.SuitesRun.Count} suite(s)\n\n");

        foreach (var suite in record.SuitesRun)
        {
            text.Append("- ").Append(suite).Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"\n## Not measured — {record.SuitesNotRun.Count} suite(s)\n\n");

        if (record.SuitesNotRun.Count == 0)
        {
            text.Append("None: every suite of the track ran. The run is still not a baseline, for the reason above.\n");
        }
        else
        {
            foreach (var suite in record.SuitesNotRun)
            {
                text.Append("- ").Append(suite).Append('\n');
            }
        }

        File.WriteAllText(target.File("scope.md"), text.ToString(), Encoding.UTF8);
    }

    private static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double Number(string[] args, string name, double fallback) =>
        Value(args, name) is { } text
        && double.TryParse(text, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;
}
