using System.Diagnostics;
using System.Globalization;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Environment;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// §20 — cold start. A first in-process call after a warm-up is not cold, so each measurement is its
/// own process: the harness launches one, the child performs exactly one operation and prints what it
/// took, and the aggregate is over launches. Process startup itself is measured by a child that does
/// nothing, and published beside the result (COLD-03).
/// </summary>
internal static class ColdStartRunner
{
    private const int Launches = 20;

    /// <summary>Runs one measurement inside a freshly started process and prints it as CSV.</summary>
    /// <remarks>
    /// Two numbers: the time from process start to the moment the operation begins, and the operation
    /// itself. Nothing is warmed, and the process exits immediately afterwards.
    /// </remarks>
    internal static int Child(string profileName, string datasetId, string operation, string? payloadPath)
    {
        var startedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var profile = Enum.Parse<ViperProfile>(profileName);
        var dataset = Corpus.Find(datasetId);

        byte[]? payload = null;

        if (operation == "deserialize")
        {
            // The payload arrives from the parent as a file. Producing it here would run the writer
            // first, and a read after a write in the same process is not a cold read.
            payload = File.ReadAllBytes(payloadPath!);
        }

        var beforeOperation = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();

        var adapter = new ViperAdapter(profile);

        if (operation == "deserialize")
        {
            _ = dataset.Deserialize(adapter, payload!);
        }
        else
        {
            _ = dataset.Serialize(adapter);
        }

        timer.Stop();

        var startupMs = (beforeOperation - startedAt).TotalMilliseconds;
        var operationMs = timer.Elapsed.TotalMilliseconds;
        var managed = GC.GetTotalAllocatedBytes(precise: true);
        var workingSet = System.Environment.WorkingSet;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{startupMs:F3},{operationMs:F3},{managed},{workingSet}"));

        return 0;
    }

    /// <summary>Launches the children, aggregates over launches, and writes the result.</summary>
    internal static int Drive(string? outputDirectory = null)
    {
        var rows = new StringBuilder(
            "profile,dataset,operation,launches,startup_ms_median,operation_ms_mean,operation_ms_median," +
            "operation_ms_min,operation_ms_max,allocated_bytes_median,working_set_median\n");

        var cases = new List<(ViperProfile Profile, string Dataset, string Operation)>();

        foreach (var profile in new[] { ViperProfile.Default, ViperProfile.Headerless, ViperProfile.ProtectedBrotli })
        {
            foreach (var dataset in new[] { "DATA-01", "DATA-02", "DATA-03" })
            {
                cases.Add((profile, dataset, "serialize"));
                cases.Add((profile, dataset, "deserialize"));
            }
        }

        var payloads = Path.Combine(Path.GetTempPath(), $"viper-cold-{System.Environment.ProcessId}");
        Directory.CreateDirectory(payloads);

        foreach (var (profile, dataset, operation) in cases)
        {
            var startup = new List<double>(Launches);
            var elapsed = new List<double>(Launches);
            var allocated = new List<double>(Launches);
            var workingSet = new List<double>(Launches);

            var payloadPath = string.Empty;

            if (operation == "deserialize")
            {
                payloadPath = Path.Combine(payloads, $"{profile}-{dataset.Replace('/', '-')}.bin");
                File.WriteAllBytes(payloadPath, Corpus.Find(dataset).Serialize(new ViperAdapter(profile)));
            }

            for (var launch = 0; launch < Launches; launch++)
            {
                var line = Launch($"--cold-child {profile} {dataset} {operation} {payloadPath}");
                var parts = line.Split(',');

                if (parts.Length != 4)
                {
                    Console.Error.WriteLine($"cold start: unusable output for {profile}/{dataset}/{operation}: {line}");
                    continue;
                }

                startup.Add(double.Parse(parts[0], CultureInfo.InvariantCulture));
                elapsed.Add(double.Parse(parts[1], CultureInfo.InvariantCulture));
                allocated.Add(double.Parse(parts[2], CultureInfo.InvariantCulture));
                workingSet.Add(double.Parse(parts[3], CultureInfo.InvariantCulture));
            }

            if (elapsed.Count == 0)
            {
                continue;
            }

            rows.Append(CultureInfo.InvariantCulture,
                $"{ViperProfiles.PlanId(profile)},{dataset},{operation},{elapsed.Count},");
            rows.Append(CultureInfo.InvariantCulture,
                $"{Median(startup):F3},{elapsed.Average():F3},{Median(elapsed):F3},");
            rows.Append(CultureInfo.InvariantCulture,
                $"{elapsed.Min():F3},{elapsed.Max():F3},{Median(allocated):F0},{Median(workingSet):F0}\n");

            Console.WriteLine(
                $"{ViperProfiles.PlanId(profile),-8} {dataset,-10} {operation,-12} " +
                $"first op {Median(elapsed),9:F3} ms   startup {Median(startup),8:F3} ms");
        }

        Directory.Delete(payloads, recursive: true);

        var destination = outputDirectory ?? Paths.Artifacts;
        var output = Path.Combine(destination, "cold-start.csv");
        Directory.CreateDirectory(destination);
        File.WriteAllText(output, rows.ToString(), Encoding.UTF8);

        Console.WriteLine($"Written to {output}");
        return 0;
    }

    private static string Launch(string arguments)
    {
        var executable = System.Environment.ProcessPath ?? "dotnet";
        var assembly = System.Reflection.Assembly.GetEntryAssembly()!.Location;

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        // A `dotnet` host needs the assembly path; an apphost already is the program.
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(assembly);
        }

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return output;
    }

    private static double Median(List<double> values)
    {
        var ordered = values.Order().ToArray();

        return ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2;
    }
}
