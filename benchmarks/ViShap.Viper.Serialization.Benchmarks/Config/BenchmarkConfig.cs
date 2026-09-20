using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Validators;

namespace ViShap.Viper.Serialization.Benchmarks.Config;

/// <summary>
/// The job every published suite runs under. Warmup, iteration and launch counts are stated here
/// rather than inherited from a default, because a baseline outlives the tool version that produced
/// it (Benchmark-Plan STAT-01).
/// </summary>
/// <remarks>
/// A shortened job exists for one purpose: proving that the whole pipeline produces every artifact
/// without spending hours on it. Its cells are real measurements of too few iterations, so a run that
/// uses it is a partial run by construction and can never become a baseline (RunTarget.Decide).
/// </remarks>
internal sealed class BenchmarkConfig : ManualConfig
{
    internal BenchmarkConfig()
        : this(Environment.Paths.Artifacts, shortened: false)
    {
    }

    internal BenchmarkConfig(string artifactsPath, bool shortened, bool serverGc = false)
    {
        Shortened = shortened;

        // SCALE-09 asks the large end of the payload curve to be measured under Server GC as well, and
        // both to be published. The job carries its own id, so a cell says which GC produced it.
        var job = shortened
            ? Job.Default
                .WithId("Shortened")
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithLaunchCount(1)

                // The pilot doubles the invocation count until one iteration reaches the target time,
                // which for a 300 ns method means over a million calls before the first measurement.
                // A shortened run exists to prove the pipeline, so it targets a tenth of that.
                .WithIterationTime(Perfolizer.Horology.TimeInterval.FromMilliseconds(50))
            : Job.Default
                .WithId("Publication")
                .WithWarmupCount(5)
                .WithIterationCount(15)
                .WithLaunchCount(1);

        AddJob(serverGc
            ? job.WithId(job.ResolvedId + "ServerGc").WithGcServer(true)
            : job);

        AddDiagnoser(MemoryDiagnoser.Default);

        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Error);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.OperationsPerSecond);
        AddColumnProvider(DefaultColumnProviders.Instance);

        AddExporter(CsvExporter.Default);
        AddExporter(CsvMeasurementsExporter.Default);
        AddExporter(JsonExporter.Full);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);

        AddLogger(ConsoleLogger.Default);

        // A publication number never comes from an unoptimized build.
        AddValidator(JitOptimizationsValidator.FailOnError);

        Orderer = new DefaultOrderer(SummaryOrderPolicy.Declared);

        // Results belong beside the run that produced them, not in whatever directory the run was
        // started from, and a publication run points this straight at its own baseline (REP-08).
        WithArtifactsPath(artifactsPath);
        WithOptions(ConfigOptions.DisableLogFile);
    }

    internal bool Shortened { get; }
}
