namespace ViShap.Viper.Serialization.Benchmarks.Environment;

/// <summary>
/// Where the harness reads from and writes to. Results land beside the benchmark project rather than
/// in whatever directory the run was started from, so a run is reproducible from anywhere.
/// </summary>
internal static class Paths
{
    /// <summary>The project path as a command line names it, for the instructions a run writes.</summary>
    internal const string ProjectName = "benchmarks/ViShap.Viper.Serialization.Benchmarks";

    /// <summary>The benchmark project directory.</summary>
    internal static string ProjectDirectory { get; } = FindProjectDirectory();

    /// <summary>Raw BenchmarkDotNet output for the current run.</summary>
    internal static string Artifacts { get; } = Path.Combine(ProjectDirectory, "BenchmarkDotNet.Artifacts");

    /// <summary>Frozen baseline packages, one directory per tag.</summary>
    internal static string Baselines { get; } = Path.Combine(ProjectDirectory, "Baselines");

    /// <summary>Partial runs, one directory per run.</summary>
    internal static string Measurements { get; } = Path.Combine(ProjectDirectory, "Measurements");

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles("ViShap.Viper.Serialization.Benchmarks.csproj").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
