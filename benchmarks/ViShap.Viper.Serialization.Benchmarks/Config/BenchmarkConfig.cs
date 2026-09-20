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
internal sealed class BenchmarkConfig : ManualConfig
{
    internal BenchmarkConfig()
    {
        AddJob(Job.Default
            .WithId("Publication")
            .WithWarmupCount(5)
            .WithIterationCount(15)
            .WithLaunchCount(1));

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

        // Results belong beside the project, not in whatever directory the run was started from.
        WithArtifactsPath(Environment.Paths.Artifacts);
        WithOptions(ConfigOptions.DisableLogFile);
    }
}
