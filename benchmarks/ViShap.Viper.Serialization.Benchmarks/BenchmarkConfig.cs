using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace ViShap.Viper.Serialization.Benchmarks;

public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        ArtifactsPath = Path.GetFullPath("BenchmarkReports");
        AddJob(Job.Default.WithId("Release-net10").WithWarmupCount(3).WithIterationCount(10));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddLogger(ConsoleLogger.Default);
        AddColumn(StatisticColumn.Mean, StatisticColumn.Median, StatisticColumn.StdDev, StatisticColumn.Min, StatisticColumn.Max, StatisticColumn.OperationsPerSecond, StatisticColumn.P95);
        AddExporter(DefaultExporters.Csv, DefaultExporters.JsonFull, MarkdownExporter.GitHub, HtmlExporter.Default);
    }
}
