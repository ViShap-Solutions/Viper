using System.Diagnostics;
using System.Globalization;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Environment;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// §22 — sustained load. Throughput and memory are sampled throughout a fixed duration, so growth
/// that a per-operation benchmark cannot see becomes a curve. The protected profile is included
/// because that is where the temporary cryptographic buffers live (§13.2).
/// </summary>
internal static class SoakRunner
{
    private static readonly TimeSpan Sample = TimeSpan.FromSeconds(10);

    internal static int Run(TimeSpan duration)
    {
        var rows = new StringBuilder(
            "profile,dataset,elapsed_s,operations,ops_per_second,managed_heap_bytes,working_set_bytes,gen0,gen1,gen2\n");

        foreach (var profile in new[] { ViperProfile.Default, ViperProfile.ProtectedBrotli })
        {
            foreach (var datasetId in new[] { "DATA-02", "DATA-03" })
            {
                Soak(profile, Corpus.Find(datasetId), duration, rows);
            }
        }

        var output = Path.Combine(Paths.Artifacts, "soak.csv");
        Directory.CreateDirectory(Paths.Artifacts);
        File.WriteAllText(output, rows.ToString(), Encoding.UTF8);

        Console.WriteLine($"Written to {output}");
        return 0;
    }

    private static void Soak(ViperProfile profile, Dataset dataset, TimeSpan duration, StringBuilder rows)
    {
        var adapter = new ViperAdapter(profile);
        var payload = dataset.Serialize(adapter);

        var total = Stopwatch.StartNew();
        var window = Stopwatch.StartNew();

        long operations = 0;
        long windowOperations = 0;

        Console.WriteLine($"{ViperProfiles.PlanId(profile)} {dataset.Id}: {duration.TotalMinutes:F1} min");

        while (total.Elapsed < duration)
        {
            var written = dataset.Serialize(adapter);
            _ = dataset.Deserialize(adapter, written);

            operations += 2;
            windowOperations += 2;

            if (window.Elapsed < Sample)
            {
                continue;
            }

            var perSecond = windowOperations / window.Elapsed.TotalSeconds;

            rows.Append(CultureInfo.InvariantCulture,
                $"{ViperProfiles.PlanId(profile)},{dataset.Id},{total.Elapsed.TotalSeconds:F1},{operations},");
            rows.Append(CultureInfo.InvariantCulture,
                $"{perSecond:F1},{GC.GetTotalMemory(forceFullCollection: false)},{System.Environment.WorkingSet},");
            rows.Append(CultureInfo.InvariantCulture,
                $"{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)}\n");

            Console.WriteLine(
                $"  {total.Elapsed.TotalSeconds,6:F0} s   {perSecond,10:F0} op/s   " +
                $"heap {GC.GetTotalMemory(false) / 1024 / 1024,5} MB   ws {System.Environment.WorkingSet / 1024 / 1024,5} MB");

            windowOperations = 0;
            window.Restart();
        }

        _ = payload;
    }
}
