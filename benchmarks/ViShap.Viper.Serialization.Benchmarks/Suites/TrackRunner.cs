using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
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
/// The one command of Benchmark-Plan §29.1 and §29.2: it runs every suite of a track in order, writes every
/// artifact of §23 into the run's own directory, and stops. There is no interactive step and no decision
/// in the middle.
/// </summary>
/// <remarks>
/// Where it writes is decided by git and by the shape of the run, never by a flag that names a version
/// (BASE-08). A tagged commit measured in full under the publication job produces a baseline; anything
/// else produces a measurement under `Measurements/`, which says in `scope.md` what it did not measure.
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
        ("A1", typeof(ProfileBufferBenchmarks)),
        ("A1", typeof(ProfileAsyncWriteBenchmarks)),
        ("A1", typeof(ProfileFramedReadBenchmarks)),
        ("A1", typeof(FrameStreamBenchmarks)),
        ("A1", typeof(HarnessFloorBenchmarks)),
        ("A1", typeof(GeneratorProfileBenchmarks)),
        ("A1", typeof(AllocationTargetBenchmarks)),

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
        ("A4", typeof(SmallFieldBenchmarks)),
        ("A4", typeof(ReferenceIdentityBenchmarks)),
        ("A4", typeof(ReferenceScopeBenchmarks)),
        ("A4", typeof(HeaderBenchmarks)),
        ("A4", typeof(MeteringBenchmarks)),
        ("A4", typeof(CycleDetectionBenchmarks)),
        ("A4", typeof(ArrayMaterializationBenchmarks)),
        ("A4", typeof(CompressionPrimitiveBenchmarks)),
        ("A4", typeof(ProtectionPrimitiveBenchmarks)),
        ("A4", typeof(DumperBenchmarks)),

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

    /// <summary>
    /// The comparative suites of Track B (§29.2), which a Track A run never measures. They are named here
    /// so that a suite registered in neither track still stops a run instead of being left out unseen. All
    /// of them are B2, the comparative core.
    /// </summary>
    private static readonly (string Stage, Type Suite)[] TrackB =
    [
        ("B2", typeof(MarketBufferedBenchmarks)),
        ("B2", typeof(MarketStreamBenchmarks)),
        ("B2", typeof(MarketBufferBenchmarks)),
        ("B2", typeof(MarketSpanBenchmarks)),
        ("B2", typeof(MarketEvolutionBenchmarks)),
    ];

    /// <summary>The names of both tracks' suites, in the order a run measures them.</summary>
    internal static IReadOnlyList<string> SuiteOrder { get; } = [.. TrackA.Concat(TrackB).Select(entry => entry.Suite.Name)];

    /// <summary>
    /// Runs one track: <c>--track A</c>, Viper alone, or <c>--track B</c>, the market of §5.2. Track B
    /// verifies the market — schema, probes, tiers, pairs — where Track A verifies the corpus and records
    /// its sizes, and it has no process-level step: cold start, first use and the soak belong to Track A.
    /// </summary>
    internal static int Run(string[] args)
    {
        bool market = string.Equals(Value(args, "--track"), "B", StringComparison.OrdinalIgnoreCase);
        var track = market ? TrackB : TrackA;
        var letter = market ? "B" : "A";

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

        var target = RunTarget.Decide(shortened, filter, allowDirty, market);
        var selected = Select(filter, track);

        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"No suite of Track {letter} matches '{filter}'. Nothing was written.");
            return 1;
        }

        if (args.Contains("--list", StringComparer.Ordinal))
        {
            Console.WriteLine($"{selected.Count} of {track.Length} suite(s) would run:");

            foreach (var entry in track.Where(entry => selected.Contains(entry.Suite)))
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

        var startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // One command produced this run. Listing every stage as if it had been run separately would
        // describe a session nobody had; the stage equivalents belong in reproduction.md, offered as an
        // alternative rather than as history.
        var command = $"dotnet run --project {Paths.ProjectName} -c Release -- --track {letter}"
                      + (shortened ? " --short" : string.Empty)
                      + (filter is null ? string.Empty : $" --filter '{filter}'")
                      + (!market && soakMinutes is > 0 and not 10 ? $" --soak {soakMinutes:F0}" : string.Empty)
                      + (allowDirty ? " --allow-dirty" : string.Empty);

        var notSelected = track.Select(entry => entry.Suite).Except(selected).Select(suite => suite.Name).ToList();
        var revision = Shell.Git("rev-parse HEAD");

        RunRecord Record(
            RunTarget written,
            IReadOnlyList<string> run,
            IReadOnlyList<string> notRun,
            bool interrupted = false,
            string? stoppedBy = null,
            IReadOnlyList<string>? unfinished = null,
            string finishedUtc = "") =>
            new(
                Kind: written.Kind.ToString(),
                Label: written.Label,
                Tag: written.Tag,
                Describe: written.Describe,
                Revision: revision,
                Dirty: written.Dirty,
                AllowDirty: written.AllowDirty,
                Reason: written.Reason,
                Track: market ? MarketReport.Track : "A — Viper alone",
                Job: shortened ? "Shortened" : "Publication",
                Filter: filter,
                StartedUtc: startedUtc,
                FinishedUtc: finishedUtc,
                Commands: [command],
                SuitesRun: run,
                SuitesNotRun: [.. notSelected, .. notRun],
                Interrupted: interrupted,
                StoppedBy: stoppedBy,
                SuitesUnfinished: unfinished);

        using var stop = StopSignal.Listen(target.WorkingDirectory, () => StopAtOnce(target));

        Announce(letter, target, selected, shortened, filter, market ? 0 : soakMinutes, stop);

        // §4 — the manifest first, so even a run that fails later says what it ran on.
        Step("manifest", () => EnvironmentManifest.Write(target.File("environment.json")));

        // §7.6 — verification before any timing. A failure here stops the run: a timing over an
        // unverified pair is not a measurement.
        if (market)
        {
            if (!VerifyMarket(target))
            {
                return 1;
            }
        }
        else if (!VerifyCorpus(target))
        {
            return 1;
        }

        // Everything timed, in order. A stop is honoured between two units and never inside one.
        var summaries = new List<Summary>();
        var units = Units(target, selected, shortened, market ? 0 : soakMinutes, summaries, processSteps: !market);
        var done = new List<Unit>();
        string? stoppedBy = null;

        foreach (var unit in units)
        {
            if (stop.Requested)
            {
                stoppedBy = stop.How;
                break;
            }

            // While a unit runs, the record on disk is the one a process that dies would leave.
            Record(
                    target.Interrupted(),
                    Suites(done),
                    Names(units.Skip(done.Count + 1)),
                    interrupted: true,
                    unfinished: [unit.Name])
                .Write(target.File("run.json"));

            unit.Work();
            done.Add(unit);
        }

        stop.Dispose();

        if (stoppedBy is not null)
        {
            var stopped = target.Interrupted();

            Console.WriteLine();
            Console.WriteLine(
                $"── Stopped by {stoppedBy} after {done.Count} of {units.Count} unit(s). Writing what was measured.");

            File.Delete(stopped.File("STOP"));

            return Finish(
                stopped.WorkingDirectory,
                Record(
                    stopped,
                    Suites(done),
                    Names(units.Skip(done.Count)),
                    interrupted: true,
                    stoppedBy: stoppedBy,
                    unfinished: [],
                    finishedUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                ResultRow.From(summaries));
        }

        try
        {
            target = target.Settle();
        }
        catch (InvalidOperationException refusal)
        {
            Console.Error.WriteLine(refusal.Message);
            target = target.Interrupted() with { Reason = "a baseline of the same tag appeared while this run measured" };
        }

        return Finish(
            target.WorkingDirectory,
            Record(
                target,
                [.. selected.Select(suite => suite.Name)],
                [],
                finishedUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ResultRow.From(summaries));
    }

    /// <summary>
    /// Track A's checks before timing: every corpus pair round-trips under every profile (§7.6), and the
    /// sizes are recorded (§14), with no timing beside them. False when a pair failed.
    /// </summary>
    private static bool VerifyCorpus(RunTarget target)
    {
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

            return false;
        }

        Step("sizes", () => SizeReport.Write(SizeReport.Collect(), target.File("payload-sizes.csv")));
        Step("format sizes", () => FormatSizeReport.Write(FormatSizeReport.Collect(), target.File("format-sizes.csv")));

        return true;
    }

    /// <summary>
    /// Track B's checks before timing (§7.4–§7.6): the models against the schema, the probes, the tiers
    /// they place each configuration in and every pair of those tiers. Writes the verification's files,
    /// the roster, the datasets and the rationales into the run, and records the verified pairs where the
    /// comparative suites read their cases. False when the schema or a pair failed.
    /// </summary>
    private static bool VerifyMarket(RunTarget target)
    {
        int failed = 0;

        Step("verify the market", () =>
        {
            var verification = MarketVerifier.VerifyAll();
            MarketVerifier.Write(verification, target.WorkingDirectory);
            MarketArtifacts.Write(target.WorkingDirectory);
            MarketCases.Record(verification);
            File.Copy(MarketCases.RecordPath, target.File(Path.GetFileName(MarketCases.RecordPath)), overwrite: true);
            failed = verification.Failed;

            foreach (var failure in verification.SchemaFailures)
            {
                Console.WriteLine($"schema: {failure}");
            }

            Console.WriteLine(
                $"{verification.Pairs.Count} pairs, {verification.Capabilities.Count} probes, " +
                $"{verification.SchemaFailures.Count} schema failures, {verification.Failed} failed.");
        });

        if (failed > 0)
        {
            Console.Error.WriteLine(
                $"{failed} schema or pair failure(s). No timing was taken: a timing over an unverified pair is not a measurement.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// One thing a run measures between two chances to stop: a suite, the Server GC pass of a suite, or a
    /// process-level step. <c>Suite</c> is set when the unit is one of the track's suites.
    /// </summary>
    private sealed record Unit(string Name, Type? Suite, Action Work);

    /// <summary>The timed units of a run, in the order it measures them.</summary>
    private static List<Unit> Units(
        RunTarget target,
        IReadOnlyList<Type> selected,
        bool shortened,
        double soakMinutes,
        List<Summary> summaries,
        bool processSteps)
    {
        var units = new List<Unit>();

        // §10, §18, §19, §21 — the timed suites, in track order, into the run's own directory.
        for (var index = 0; index < selected.Count; index++)
        {
            var suite = selected[index];
            var position = $"[{index + 1}/{selected.Count}]";

            units.Add(new Unit(suite.Name, suite, () =>
            {
                Console.WriteLine();
                Console.WriteLine($"── {position} {suite.Name}");

                summaries.Add(BenchmarkRunner.Run(suite, new BenchmarkConfig(target.WorkingDirectory, shortened)));
            }));
        }

        // §19 SCALE-09 — the large end of the payload curve again, under Server GC, so both are published
        // and a reader can see what the GC mode is worth. It is the same suite and the same corpus; only
        // the job differs, and the job id travels with every cell. It exports into a directory of its
        // own, because BenchmarkDotNet names an export after the suite and would overwrite the first pass.
        if (selected.Contains(typeof(PayloadSizeScalingBenchmarks)))
        {
            units.Add(new Unit(ExportRecovery.ServerGcPass(nameof(PayloadSizeScalingBenchmarks)), null, () =>
            {
                Console.WriteLine();
                Console.WriteLine("── SCALE-09  PayloadSizeScalingBenchmarks under Server GC");

                summaries.Add(BenchmarkRunner.Run(
                    typeof(PayloadSizeScalingBenchmarks),
                    new BenchmarkConfig(
                        Path.Combine(target.WorkingDirectory, ExportRecovery.ServerGcDirectory),
                        shortened,
                        serverGc: true)));
            }));
        }

        if (!processSteps)
        {
            return units;
        }

        // §18 — the cold half of the member plan, which no repeated-invocation harness can measure.
        units.Add(StepUnit("member-plan construction", () => ContractColdRunner.Run(target.WorkingDirectory)));

        // §20 WL-10 — the first use of each type in a warm process, reflected and generated, one process
        // per launch.
        units.Add(StepUnit("first use", () => FirstUseRunner.Drive(target.WorkingDirectory)));

        // §20 — cold start, one process per measurement.
        units.Add(StepUnit("cold start", () => ColdStartRunner.Drive(target.WorkingDirectory)));

        // §22 — sustained load. Skipped in a shortened run, because ten minutes of soak proves nothing
        // about a pipeline and the shortened run exists to prove the pipeline.
        if (soakMinutes > 0)
        {
            units.Add(StepUnit(
                $"soak {soakMinutes:F0} min",
                () => SoakRunner.Run(TimeSpan.FromMinutes(soakMinutes), target.WorkingDirectory)));
        }

        return units;
    }

    private static Unit StepUnit(string name, Action work) => new(name, null, () => Step(name, work));

    /// <summary>The track suites among the units that ran.</summary>
    private static List<string> Suites(IEnumerable<Unit> units) =>
        [.. units.Where(unit => unit.Suite is not null).Select(unit => unit.Name)];

    /// <summary>Every unit a run did not reach — suites, the Server GC pass and steps alike.</summary>
    private static List<string> Names(IEnumerable<Unit> units) => [.. units.Select(unit => unit.Name)];

    /// <summary>
    /// The second Ctrl+C. Nothing more is written: the suites that finished are in BenchmarkDotNet's
    /// exports, and the record on disk already names the unit that was cut off, so <c>--report</c>
    /// rebuilds the rest. BenchmarkDotNet ends the child it was measuring with when this process exits.
    /// </summary>
    private static void StopAtOnce(RunTarget target)
    {
        Console.WriteLine();
        Console.WriteLine("── Ctrl+C again: stopped at once.");
        Console.WriteLine(
            $"   The suites that finished are exported in {target.WorkingDirectory}. To turn them into this " +
            "run's results and report:");
        Console.WriteLine($"   dotnet run --project {Paths.ProjectName} -c Release -- --report \"{target.WorkingDirectory}\"");

        System.Environment.Exit(StopSignal.ImmediateExitCode);
    }

    /// <summary>
    /// <c>--report</c> on a run with no <c>results.csv</c>: the process ended before it wrote one. The
    /// cells come back from BenchmarkDotNet's exports, the record is the one the run kept current while
    /// it worked, and what follows is exactly what a run stopped on request writes — always a
    /// measurement.
    /// </summary>
    internal static int Recover(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var baselines = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Paths.Baselines)) + Path.DirectorySeparatorChar;

        if (directory.StartsWith(baselines, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"{directory} is under Baselines/ and has no results.csv. A baseline is never rebuilt from " +
                "its exports: a run that did not finish is not one. Nothing was written.");

            return 1;
        }

        var order = SuiteOrder.ToList();
        var exports = ExportRecovery.Read(directory, order);
        var path = Path.Combine(directory, "run.json");
        var kept = File.Exists(path) ? RunRecord.Read(path) : Unrecorded(directory, exports);

        // A unit whose export exists finished, whatever the record kept while it ran says: the process
        // can end between the export and the next update of the record.
        var exported = exports
            .Select(export => export.ServerGc ? ExportRecovery.ServerGcPass(export.Suite) : export.Suite)
            .ToHashSet(StringComparer.Ordinal);

        var suitesRun = kept.SuitesRun
            .Concat(exports.Where(export => !export.ServerGc).Select(export => export.Suite))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(suite => order.IndexOf(suite) is >= 0 and var index ? index : int.MaxValue)
            .ToList();

        var finished = exports.Count > 0 ? exports.Max(export => export.WrittenUtc) : Directory.GetLastWriteTimeUtc(directory);

        var record = kept with
        {
            Kind = nameof(RunKind.Measurement),
            Label = Path.GetFileName(directory),
            Reason = kept.Interrupted
                ? kept.Reason
                : kept.IsBaseline
                    ? "the run was interrupted before every suite of the track ran"
                    : $"{kept.Reason}; the run was interrupted",
            FinishedUtc = finished.ToString("O", CultureInfo.InvariantCulture),
            SuitesRun = suitesRun,
            SuitesNotRun = [.. kept.SuitesNotRun.Where(name => !exported.Contains(name))],
            SuitesUnfinished = [.. (kept.SuitesUnfinished ?? []).Where(name => !exported.Contains(name))],
            Interrupted = true,
            StoppedBy = kept.StoppedBy ?? "the end of the process, before the run finished",
        };

        Console.WriteLine($"No results.csv in {directory}: rebuilding it from {exports.Count} BenchmarkDotNet export(s).");

        return Finish(directory, record, [.. exports.SelectMany(export => export.Rows)]);
    }

    /// <summary>
    /// The record of a run that left none, because it died before this harness kept one current. What
    /// the manifest said at its start is all there is to go on.
    /// </summary>
    private static RunRecord Unrecorded(string directory, IReadOnlyList<ExportRecovery.SuiteExport> exports)
    {
        string revision = "unknown", started = string.Empty;
        bool dirty = false;
        var manifest = Path.Combine(directory, "environment.json");

        if (File.Exists(manifest))
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            var root = json.RootElement;
            revision = root.TryGetProperty("revision", out var r) ? r.ToString() : revision;
            started = root.TryGetProperty("capturedUtc", out var c) ? c.ToString() : started;
            dirty = root.TryGetProperty("dirty", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True;
        }

        var label = Path.GetFileName(directory);

        return new RunRecord(
            Kind: nameof(RunKind.Measurement),
            Label: label,
            Tag: null,
            Describe: label,
            Revision: revision,
            Dirty: dirty,
            AllowDirty: false,
            Reason: "the run left no record of itself, so its exports and its manifest are all that describe it; " +
                    "the run was interrupted",
            Track: "A — Viper alone",
            Job: exports.SelectMany(export => export.Rows).Select(row => row.Job).FirstOrDefault() ?? "unknown",
            Filter: null,
            StartedUtc: started,
            FinishedUtc: string.Empty,
            Commands: ["unknown — the run ended before it recorded its command"],
            SuitesRun: [],
            SuitesNotRun: [.. TrackA.Select(entry => entry.Suite.Name)],
            Interrupted: true);
    }

    /// <summary>
    /// Writes everything a run leaves behind, from its cells and its record: the archive of the export,
    /// the tables, <c>run.json</c>, the manifest, the reproduction instructions, the scope of a
    /// measurement and the report. A run that finished, one stopped on request and one rebuilt by
    /// <c>--report</c> all end here, so they cannot differ in what they write.
    /// </summary>
    private static int Finish(string directory, RunRecord record, IReadOnlyList<ResultRow> rows)
    {
        // The BenchmarkDotNet export is 27 MB of per-iteration data for a full run, and only a recovery
        // reads it back: the report and the charts are built from results.csv. A baseline is immutable,
        // so a diff over those files would never be taken either. Archived, it costs a few megabytes per
        // release instead of tens, and the raw measurements are still there for anyone who wants them.
        Step("archive the BenchmarkDotNet export", () => Archive(directory));

        ResultRow.Write(rows, Path.Combine(directory, "results.csv"));
        ResultRow.WriteMemory(rows, Path.Combine(directory, "memory.csv"));
        if (record.Track != MarketReport.Track)
        {
            ResultRow.Write([.. rows.Where(row => row.IsComponent)], Path.Combine(directory, "components.csv"));
        }

        record.Write(Path.Combine(directory, "run.json"));
        Manifest(directory, record, rows);
        Reproduction(directory, record);

        if (!record.IsBaseline)
        {
            Scope(directory, record);
        }

        // The report is generated twice and compared, so a generator that stopped being a pure view over
        // the raw files says so here (REP-02).
        var drifted = ReportWriter.GenerateAndCheck(directory);

        foreach (var file in drifted)
        {
            Console.Error.WriteLine(
                $"{file} differs between two generations from the same raw files: the report is not a view " +
                "over them. This is a harness defect, not a measurement.");
        }

        int failedCells = rows.Count(row => row.State != "Supported");

        Console.WriteLine();
        Console.WriteLine($"{record.Kind} {record.Label} — {rows.Count} cells, {failedCells} without a number.");
        Console.WriteLine($"Written to {directory}");
        Console.WriteLine($"Elapsed {Elapsed(record):hh\\:mm\\:ss}");

        if (!record.IsBaseline)
        {
            Console.WriteLine($"Not a baseline: {record.Reason}.");
        }

        if (record.Interrupted)
        {
            Console.WriteLine(
                $"Interrupted by {record.StoppedBy}: {record.SuitesRun.Count} suite(s) measured; what did not run " +
                "is listed in scope.md.");

            return 2;
        }

        return failedCells == 0 && drifted.Count == 0 ? 0 : 1;
    }

    private static TimeSpan Elapsed(RunRecord record) =>
        DateTime.TryParse(record.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
        && DateTime.TryParse(record.FinishedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var finished)
        && finished >= started
            ? finished - started
            : TimeSpan.Zero;

    /// <summary>
    /// Packs the BenchmarkDotNet export into one file beside the results it came from: the suites at the
    /// root of the archive, the Server GC pass under <c>server-gc/</c>. An archive that already exists
    /// keeps what it holds, and a run that produced no export leaves nothing behind.
    /// </summary>
    private static void Archive(string directory)
    {
        var loose = ExportRecovery.LooseFiles(directory).ToList();

        if (loose.Count == 0)
        {
            return;
        }

        var archive = Path.Combine(directory, "results.zip");
        var partial = archive + ".partial";
        File.Delete(partial);

        using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            if (File.Exists(archive))
            {
                using var previous = ZipFile.OpenRead(archive);

                foreach (var entry in previous.Entries.Where(entry => loose.All(file => file.Key != entry.FullName)))
                {
                    using var source = entry.Open();
                    using var copy = zip.CreateEntry(entry.FullName, CompressionLevel.SmallestSize).Open();
                    source.CopyTo(copy);
                }
            }

            foreach (var (key, path) in loose)
            {
                zip.CreateEntryFromFile(path, key, CompressionLevel.SmallestSize);
            }
        }

        // The loose files go only once the archive is whole, so a process that dies here leaves one or
        // the other intact.
        File.Move(partial, archive, overwrite: true);

        long packed = new FileInfo(archive).Length;
        long size = loose.Sum(file => new FileInfo(file.Path).Length);

        foreach (var folder in ExportRecovery.ExportDirectories(directory))
        {
            Directory.Delete(folder, recursive: true);
        }

        var serverGc = Path.Combine(directory, ExportRecovery.ServerGcDirectory);

        if (Directory.Exists(serverGc) && !Directory.EnumerateFileSystemEntries(serverGc).Any())
        {
            Directory.Delete(serverGc);
        }

        Console.WriteLine(
            $"   {size / 1024:N0} KB of export packed into {packed / 1024:N0} KB, " +
            $"{(size > 0 ? (double)size / packed : 0):F1}x");
    }

    private static void Announce(
        string letter, RunTarget target, IReadOnlyList<Type> selected, bool shortened, string? filter, double soakMinutes, StopSignal stop)
    {
        Console.WriteLine($"Track {letter} — {selected.Count} suite(s), job {(shortened ? "Shortened" : "Publication")}");
        Console.WriteLine($"  destination  {target.Directory}");
        Console.WriteLine($"  kind         {target.Kind} — {target.Reason}");
        Console.WriteLine($"  filter       {filter ?? "none"}");
        Console.WriteLine($"  soak         {(soakMinutes > 0 ? $"{soakMinutes:F0} min" : "skipped")}");
        Console.WriteLine($"  working tree {(target.Dirty ? "dirty" : "clean")}"
                          + (target.Dirty && target.AllowDirty ? ", allowed by --allow-dirty" : string.Empty));

        if (target.IsBaseline)
        {
            Console.WriteLine($"  measured in  {target.WorkingDirectory}, moved to the destination once every suite has run");
        }

        Console.WriteLine(stop.ReadsControlC
            ? $"  to stop      Ctrl+C, or a file named STOP in {target.WorkingDirectory}: the current suite finishes " +
              "and what was measured is written. Ctrl+C twice stops at once."
            : $"  to stop      a file named STOP in {target.WorkingDirectory}: the current suite finishes and what " +
              "was measured is written. Standard input is not a console, so Ctrl+C stops at once.");
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
    /// profile matrix plus every curve, and <c>*Header*,*Budget*</c> is two suites. Track B's suites are
    /// all of stage <c>b2</c>.
    /// </summary>
    private static IReadOnlyList<Type> Select(string? filter, (string Stage, Type Suite)[] track)
    {
        if (filter is null)
        {
            return [.. track.Select(entry => entry.Suite)];
        }

        var tokens = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return
        [
            .. track
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
    /// Suites that carry benchmarks but belong to neither track. A run refuses rather than quietly
    /// measuring less than it claims.
    /// </summary>
    private static IReadOnlyList<string> Unregistered() =>
    [
        .. typeof(TrackRunner).Assembly.GetTypes()
            .Where(type => type is { IsPublic: true, IsAbstract: false })
            .Where(type => type.GetMethods().Any(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .Where(type => !TrackA.Concat(TrackB).Any(entry => entry.Suite == type))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal),
    ];

    private static void Manifest(string directory, RunRecord record, IReadOnlyList<ResultRow> rows)
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
        text.Append(CultureInfo.InvariantCulture, $"- elapsed: {Elapsed(record):hh\\:mm\\:ss}\n");
        text.Append(CultureInfo.InvariantCulture, $"- suites run: {record.SuitesRun.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"- timed cells: {rows.Count}\n");
        text.Append(CultureInfo.InvariantCulture,
            $"- cells without a number: {rows.Count(row => row.State != "Supported")}\n");
        text.Append(CultureInfo.InvariantCulture, $"- why this kind: {record.Reason}\n\n");
        text.Append("The machine, the runtime, the GC mode and every pinned package are in `environment.json`.\n");

        File.WriteAllText(Path.Combine(directory, "manifest.md"), text.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// §26 — the commands that produced this run, in the order they ran, naming only what the repository
    /// contains, because a reader of this directory has the repository and nothing else.
    /// </summary>
    private static void Reproduction(string directory, RunRecord record)
    {
        var checkout = record.Tag ?? record.Revision;
        var run = $"dotnet run --project {Paths.ProjectName} -c Release --";
        var commands = string.Join(System.Environment.NewLine, record.Commands);
        var folder = record.Kind == nameof(RunKind.Baseline) ? "Baselines" : "Measurements";
        var tagged = record.Tag is null ? string.Empty : $", tag `{record.Tag}`";
        bool market = record.Track == MarketReport.Track;

        var whole = string.Join('\n', market
            ? new[]
            {
                "The last line is the whole of it: it writes the manifest, verifies the market — the models against",
                "the schema, the probes, the tiers and every pair — runs the comparative suites in order, and",
                "generates this directory's report.",
            }
            : new[]
            {
                "The last line is the whole of it: it writes the manifest, verifies every dataset, records the",
                "sizes, runs the suites in order, measures cold start and sustained load, and generates this",
                "directory's report and charts.",
            });

        var parts = string.Join('\n', market
            ? new[]
            {
                $"{run} --verify --market   # the market's schema, probes, tiers and pairs, no timing",
                $"{run} --manifest          # the environment",
                $"{run} --track B --filter '*Evolution*'",
            }
            : new[]
            {
                $"{run} --verify          # round trips, no timing",
                $"{run} --sizes           # the size table",
                $"{run} --manifest        # the environment",
                $"{run} --contract-cold   # member-plan construction",
                $"{run} --first-use       # first use of a type, reflected and generated",
                $"{run} --cold            # cold start",
                $"{run} --soak 10         # sustained load",
                $"{run} --track A --filter '*Algorithm*'",
            });

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

            {whole}

            ## Re-running one part alone

            ```text
            {parts}
            {run} --report {folder}/{record.Label}
            ```

            A filter narrows the run to the suites whose name matches it, and a narrowed run is a
            measurement: it lands under `Measurements/` and never under `Baselines/`. The last line regenerates the
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

        File.WriteAllText(Path.Combine(directory, "reproduction.md"), text, Encoding.UTF8);
    }

    /// <summary>
    /// What a measurement did not measure. Without it a directory holding half the suites could be read
    /// as a baseline that happens to be small (REP-13).
    /// </summary>
    private static void Scope(string directory, RunRecord record)
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

        if (record.Interrupted)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## Interrupted — by {record.StoppedBy}\n\n");
            text.Append("The run ended before its last suite. It is a measurement whatever its tag and its job, and\n");
            text.Append("the suites and steps it did not reach are listed below with any a filter left out.\n");

            foreach (var suite in record.SuitesUnfinished ?? [])
            {
                text.Append("\n- ").Append(suite).Append(" was cut off in the middle. BenchmarkDotNet exports a suite only\n");
                text.Append("  when it ends, so none of its cells is in this directory.\n");
            }
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

        File.WriteAllText(Path.Combine(directory, "scope.md"), text.ToString(), Encoding.UTF8);
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
