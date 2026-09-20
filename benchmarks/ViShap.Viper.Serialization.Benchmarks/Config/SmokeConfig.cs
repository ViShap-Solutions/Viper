using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Validators;

namespace ViShap.Viper.Serialization.Benchmarks.Config;

/// <summary>
/// The job a smoke run uses: one invocation per benchmark, to prove the switcher still runs (LAY-04).
/// Its numbers are meaningless and are never published, which is why it writes to a directory of its
/// own rather than beside the publication artifacts.
/// </summary>
/// <remarks>
/// A job named on the command line as <c>--job Dry</c> is ignored whenever a config declares one of its
/// own, and <see cref="BenchmarkConfig"/> declares one on purpose (STAT-01). A smoke run therefore needs
/// its own config rather than a flag, and the runner picks between the two.
/// </remarks>
internal sealed class SmokeConfig : ManualConfig
{
    internal SmokeConfig()
    {
        AddJob(Job.Dry
            .WithId("Smoke")
            .WithStrategy(RunStrategy.ColdStart)
            .WithWarmupCount(1)
            .WithIterationCount(1)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithLaunchCount(1));

        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);

        // A smoke run is still a Release run: an unoptimized build would prove nothing about the harness.
        AddValidator(JitOptimizationsValidator.FailOnError);

        Orderer = new DefaultOrderer(SummaryOrderPolicy.Declared);

        WithArtifactsPath(Path.Combine(Environment.Paths.Artifacts, "smoke"));
        WithOptions(ConfigOptions.DisableLogFile | ConfigOptions.JoinSummary);
    }
}
