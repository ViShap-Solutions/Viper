using System.Diagnostics;
using System.Globalization;
using System.Text;
using ViShap.Viper.Engine;
using ViShap.Viper.Serialization.Benchmarks.Environment;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-04, the cold half — what building a member plan costs the first time a type is seen.
/// </summary>
/// <remarks>
/// A contract is cached per type for the life of the process, so one type can be built cold exactly
/// once and no repeated-invocation harness can measure it. The record is therefore three kinds of row.
/// The first is the first construction of the process, which also pays the one-time JIT of the
/// construction path and is an order of magnitude larger than anything after it. The second is a
/// distribution over closed generic instantiations of one shape — an unlimited supply of unseen types
/// with identical members — taken with the path already warm. The third is one observation per ordinary
/// type of the corpus, which is what shows how construction scales with member count, and what the
/// generic rows are checked against: the two must agree at equal member counts, or the instantiations
/// are measuring something a plain type does not.
/// <para>
/// Explains the first-use column of DIFF-03 and the per-type part of every COLD-01 cell.
/// </para>
/// </remarks>
internal static class ContractColdRunner
{
    private const int Warmup = 20;
    private const int Samples = 200;

    private static readonly Type[] Markers = [typeof(M0), typeof(M1), typeof(M2), typeof(M3)];

    /// <summary>
    /// The types of the corpus, each built cold once, in a process where nothing else has touched a
    /// contract. One observation per type, but many types and many member counts, which is what shows
    /// the slope.
    /// </summary>
    private static readonly Type[] PlainTypes =
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

        // The member-count series of SCALE-04: the same cycle of member types at five sizes under both
        // layouts, which is what turns the single observations above into a slope.
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

    internal static int Run(string? outputDirectory = null)
    {
        var rows = new StringBuilder(
            "kind,type,members,samples,mean_us,median_us,p95_us,min_us,max_us,allocated_bytes_median\n");

        Console.WriteLine(
            $"{"Kind",-10} {"Type",-22} {"Members",8} {"Samples",8} {"Mean us",10} {"Median us",10} " +
            $"{"P95 us",10} {"Min us",10} {"Max us",10} {"Alloc B",10}");
        Console.WriteLine(new string('-', 124));

        // The very first contract of a process also pays the one-time JIT of the construction path,
        // which is an order of magnitude larger than the construction itself. It belongs to COLD-01 and
        // is published as its own row rather than smeared over whichever type happened to come first.
        MeasureOnce(typeof(FirstInProcess), rows, kind: "first-in-process");

        // A distribution needs repeated samples and a type can be built cold only once, so these rows
        // are closed generic instantiations of one shape: an unlimited supply of unseen types with
        // identical members.
        Measure("generic", "ColdShape<T>", index => typeof(ColdShape<>).MakeGenericType(Marker(index)), rows);
        Measure("generic", "ColdKeyedShape<T>", index => typeof(ColdKeyedShape<>).MakeGenericType(Marker(index)), rows);

        // Ordinary types, once each, with the path already warm: one observation per type, but enough
        // types and member counts to show the slope, and the check on the generic rows above.
        foreach (var type in PlainTypes)
        {
            MeasureOnce(type, rows);
        }

        var destination = outputDirectory ?? Paths.Artifacts;
        Directory.CreateDirectory(destination);
        var output = Path.Combine(destination, "contract-cold.csv");
        File.WriteAllText(output, rows.ToString(), Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine($"Written to {output}");

        return 0;
    }

    private static void Measure(string kind, string name, Func<int, Type> manufacture, StringBuilder rows)
    {
        var contract = TypeContractCache.Get(manufacture(0));

        for (var index = 1; index < Warmup; index++)
        {
            _ = TypeContractCache.Get(manufacture(index));
        }

        var elapsed = new double[Samples];
        var allocated = new double[Samples];

        for (var sample = 0; sample < Samples; sample++)
        {
            // Manufacturing the type is reflection of its own and stays outside the timed region.
            var type = manufacture(Warmup + sample);

            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();

            _ = TypeContractCache.Get(type);

            timer.Stop();

            elapsed[sample] = timer.Elapsed.TotalMicroseconds;
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var ordered = elapsed.Order().ToArray();
        double mean = elapsed.Average();
        double median = Median(ordered);
        double p95 = ordered[(int)(ordered.Length * 0.95)];

        double allocationMedian = Median(allocated.Order().ToArray());

        rows.Append(CultureInfo.InvariantCulture,
            $"{kind},{name},{contract.Members.Length},{Samples},{mean:F3},{median:F3},{p95:F3}," +
            $"{ordered[0]:F3},{ordered[^1]:F3},{allocationMedian:F0}\n");

        Console.WriteLine(
            $"{kind,-10} {name,-22} {contract.Members.Length,8} {Samples,8} {mean,10:F3} {median,10:F3} " +
            $"{p95,10:F3} {ordered[0],10:F3} {ordered[^1],10:F3} {allocationMedian,10:F0}");
    }

    /// <summary>One type, built cold once. The row states a sample size of one and carries no spread.</summary>
    private static void MeasureOnce(Type type, StringBuilder rows, string? kind = null)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();

        var contract = TypeContractCache.Get(type);

        timer.Stop();

        double elapsed = timer.Elapsed.TotalMicroseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        kind ??= contract.Layout == MemberLayout.Keyed ? "keyed" : "positional";

        rows.Append(CultureInfo.InvariantCulture,
            $"{kind},{type.Name},{contract.Members.Length},1,{elapsed:F3},{elapsed:F3},{elapsed:F3}," +
            $"{elapsed:F3},{elapsed:F3},{allocated}\n");

        Console.WriteLine(
            $"{kind,-10} {type.Name,-22} {contract.Members.Length,8} {1,8} {elapsed,10:F3} {elapsed,10:F3} " +
            $"{elapsed,10:F3} {elapsed,10:F3} {elapsed,10:F3} {allocated,10}");
    }

    /// <summary>
    /// A type argument unique to <paramref name="index"/>, built as six base-4 digits of nested
    /// generics. Six digits give 4096 distinct arguments, which is more than a run consumes.
    /// </summary>
    private static Type Marker(int index)
    {
        var argument = Markers[index % Markers.Length];
        index /= Markers.Length;

        for (var digit = 0; digit < 5; digit++)
        {
            argument = typeof(Tag<,>).MakeGenericType(Markers[index % Markers.Length], argument);
            index /= Markers.Length;
        }

        return argument;
    }

    private static double Median(double[] ordered) =>
        ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2;

    private sealed class M0
    {
    }

    private sealed class M1
    {
    }

    private sealed class M2
    {
    }

    private sealed class M3
    {
    }

    /// <summary>The type whose construction absorbs the one-time JIT of the construction path.</summary>
    private sealed class FirstInProcess
    {
        public int Value { get; set; }
    }

    /// <summary>Carries nothing: it exists only to make its instantiations distinct types.</summary>
    private sealed class Tag<T1, T2>
    {
    }

    /// <summary>
    /// The shape whose contract is built. <typeparamref name="T"/> takes no part in the members, so
    /// every instantiation presents the same eight members to the contract builder and differs from
    /// the others only in identity.
    /// </summary>
    private sealed class ColdShape<T>
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Note { get; set; }

        public long Sequence { get; set; }

        public double Weight { get; set; }

        public decimal Price { get; set; }

        public DateTime CreatedUtc { get; set; }

        public bool Published { get; set; }
    }

    /// <summary>The same members under a keyed contract, so the two layouts are measured as a pair.</summary>
    [BinaryContract]
    private sealed class ColdKeyedShape<T>
    {
        [BinaryKey(1)]
        public Guid Id { get; set; }

        [BinaryKey(2)]
        public string Name { get; set; } = string.Empty;

        [BinaryKey(3)]
        public string? Note { get; set; }

        [BinaryKey(4)]
        public long Sequence { get; set; }

        [BinaryKey(5)]
        public double Weight { get; set; }

        [BinaryKey(6)]
        public decimal Price { get; set; }

        [BinaryKey(7)]
        public DateTime CreatedUtc { get; set; }

        [BinaryKey(8)]
        public bool Published { get; set; }
    }
}
