using BenchmarkDotNet.Running;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Environment;
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

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new BenchmarkConfig());
        return 0;
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

    private static int Manifest()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "environment.json");
        EnvironmentManifest.Write(output);

        Console.WriteLine(File.ReadAllText(output));
        Console.WriteLine($"Written to {output}");

        return 0;
    }
}
