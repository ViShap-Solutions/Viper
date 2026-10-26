using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using ViShap.Viper.Contracts;
using ViShap.Viper.Engine;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.Environment;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// WL-10 and COLD-04 — the first operation for a type not seen before, in a process already warm for
/// another type, under the reflected contracts of B-P0 and the generated ones of B-P0g.
/// </summary>
/// <remarks>
/// A codec and a contract are cached for the life of the process, so a type is first used exactly once
/// per process, and the two profiles cannot share one: whichever ran first would leave the other its
/// codecs. Each launch is therefore its own process under one profile. It instantiates the context under
/// B-P0g, builds the options and the serializer, writes and reads a warm-up type so the
/// path every type shares is compiled, and then writes and reads each type once, leaves first, so a
/// type's row holds its own first use and not that of the types it reaches. The aggregate is over
/// launches.
/// <para>
/// Explains the per-type part of the COLD-01 cells, and is the first-use half of DIFF-03; the
/// construction alone, with no codec around it, is what <c>ContractColdRunner</c> reports.
/// </para>
/// </remarks>
internal static class FirstUseRunner
{
    private const int Launches = 10;

    private static readonly ViperProfile[] Profiles = [ViperProfile.Default, ViperProfile.Generated];

    /// <summary>
    /// The member-encoded types of the corpus, a type always after the types it reaches, so its row is
    /// its own first use.
    /// </summary>
    private static readonly Type[] Types =
    [
        typeof(TinyFlat),
        typeof(Address),
        typeof(Measurement),
        typeof(ScalarRecord),
        typeof(DeepNode),
        typeof(DagNode),
        typeof(TextEvent),
        typeof(BatchEvent),
        typeof(BlobEnvelope),
        typeof(NumericArrays),
        typeof(NullSparse),
        typeof(CollectionZoo),
        typeof(TimeAndNumerics),
        typeof(MediumObject),
        typeof(WideObject),
        typeof(KeyedLine),
        typeof(KeyedOrder),
        typeof(KeyedOrderV2),
        typeof(WidePositional005),
        typeof(WidePositional020),
        typeof(WidePositional050),
        typeof(WidePositional100),
        typeof(WideKeyed005),
        typeof(WideKeyed020),
        typeof(WideKeyed050),
        typeof(WideKeyed100),
        typeof(WideKeyed200),
    ];

    /// <summary>
    /// One launch: prints a line per measurement, <c>type,operation,microseconds,allocated bytes</c>.
    /// The context's instance is the row of type <c>(context)</c>, under B-P0g only, and the options and
    /// the serializer built over it the row of type <c>(serializer)</c>.
    /// </summary>
    internal static int Child(string profileName)
    {
        var profile = Enum.Parse<ViperProfile>(profileName);
        var output = new StringBuilder();

        long before;
        var timer = new Stopwatch();

        // A context creates its contracts when its Default is first read. That is its own row, so the
        // serializer's row is the options and the serializer alone under both profiles.
        if (ViperProfiles.ReflectedTwin(profile) is not null)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            timer.Start();
            _ = BenchmarkContracts.Default;
            timer.Stop();
            Line(output, "(context)", "instantiate", timer.Elapsed.TotalMicroseconds, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        before = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        var serializer = new BinarySerializer(ViperProfiles.Options(profile));
        timer.Stop();
        Line(output, "(serializer)", "construct", timer.Elapsed.TotalMicroseconds, GC.GetAllocatedBytesForCurrentThread() - before);

        _ = serializer.Deserialize<FirstUseWarmup>(serializer.Serialize(new FirstUseWarmup { Id = 1, Name = "warm", Values = [1, 2, 3] }));

        var write = typeof(FirstUseRunner).GetMethod(nameof(Write), BindingFlags.NonPublic | BindingFlags.Static)!;
        var read = typeof(FirstUseRunner).GetMethod(nameof(Read), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var type in Types)
        {
            // The delegates and the value are prepared outside the timed region; the first call through
            // them is the type's first use.
            var serialize = write.MakeGenericMethod(type).CreateDelegate<Func<BinarySerializer, object, byte[]>>();
            var deserialize = read.MakeGenericMethod(type).CreateDelegate<Func<BinarySerializer, byte[], object?>>();
            var value = Activator.CreateInstance(type)!;

            before = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            var payload = serialize(serializer, value);
            timer.Stop();
            Line(output, type.Name, "serialize", timer.Elapsed.TotalMicroseconds, GC.GetAllocatedBytesForCurrentThread() - before);

            before = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            _ = deserialize(serializer, payload);
            timer.Stop();
            Line(output, type.Name, "deserialize", timer.Elapsed.TotalMicroseconds, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Console.Write(output.ToString());
        return 0;
    }

    /// <summary>Launches the children, aggregates over launches, and writes <c>first-use.csv</c>.</summary>
    internal static int Drive(string? outputDirectory = null)
    {
        var observations = new Dictionary<(ViperProfile Profile, string Type, string Operation), List<(double Us, double Bytes)>>();
        var order = new List<(ViperProfile, string, string)>();

        foreach (var profile in Profiles)
        {
            for (var launch = 0; launch < Launches; launch++)
            {
                var lines = ColdStartRunner.Launch($"--first-use-child {profile}")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var line in lines)
                {
                    var parts = line.Split(',');

                    if (parts.Length != 4)
                    {
                        Console.Error.WriteLine($"first use: unusable output under {profile}: {line}");
                        continue;
                    }

                    var key = (profile, parts[0], parts[1]);

                    if (!observations.TryGetValue(key, out var list))
                    {
                        observations[key] = list = new List<(double, double)>(Launches);
                        order.Add(key);
                    }

                    list.Add((double.Parse(parts[2], CultureInfo.InvariantCulture), double.Parse(parts[3], CultureInfo.InvariantCulture)));
                }
            }
        }

        var shapes = Types.ToDictionary(type => type.Name, type => TypeContractCache.Get(type));

        var rows = new StringBuilder(
            "profile,type,members,layout,operation,launches,median_us,mean_us,min_us,max_us,allocated_bytes_median\n");

        Console.WriteLine(
            $"{"Profile",-7} {"Type",-20} {"Operation",-12} {"Median us",10} {"Min us",10} {"Max us",10} {"Alloc B",10}");
        Console.WriteLine(new string('-', 86));

        foreach (var (profile, type, operation) in order)
        {
            var samples = observations[(profile, type, operation)];
            var times = samples.Select(sample => sample.Us).Order().ToArray();
            var bytes = samples.Select(sample => sample.Bytes).Order().ToArray();

            var (members, layout) = shapes.TryGetValue(type, out var contract)
                ? (contract.Members.Length.ToString(CultureInfo.InvariantCulture), contract.Layout == MemberLayout.Keyed ? "keyed" : "positional")
                : (string.Empty, string.Empty);

            rows.Append(CultureInfo.InvariantCulture,
                $"{ViperProfiles.PlanId(profile)},{type},{members},{layout},{operation},{times.Length},");
            rows.Append(CultureInfo.InvariantCulture,
                $"{Median(times):F3},{times.Average():F3},{times[0]:F3},{times[^1]:F3},{Median(bytes):F0}\n");

            Console.WriteLine(
                $"{ViperProfiles.PlanId(profile),-7} {type,-20} {operation,-12} {Median(times),10:F3} " +
                $"{times[0],10:F3} {times[^1],10:F3} {Median(bytes),10:F0}");
        }

        var destination = outputDirectory ?? Paths.Artifacts;
        Directory.CreateDirectory(destination);
        var output = Path.Combine(destination, "first-use.csv");
        File.WriteAllText(output, rows.ToString(), Encoding.UTF8);

        Console.WriteLine($"Written to {output}");
        return 0;
    }

    private static byte[] Write<T>(BinarySerializer serializer, object value) => serializer.Serialize((T)value);

    private static object? Read<T>(BinarySerializer serializer, byte[] payload) => serializer.Deserialize<T>(payload);

    private static void Line(StringBuilder output, string type, string operation, double microseconds, long allocated) =>
        output.Append(CultureInfo.InvariantCulture, $"{type},{operation},{microseconds:F3},{allocated}\n");

    private static double Median(double[] ordered) =>
        ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2;
}
