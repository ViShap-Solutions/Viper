using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-18 — a primitive array read two ways (Contract §17): into an array allocated at its final
/// length, which the engine does when the bytes that remain back the count, and through the pooled
/// path it takes when they do not — elements gathered in a rented buffer as they arrive, then copied
/// once into an array of the final length.
/// </summary>
/// <remarks>
/// Both cells read the same payload of <see cref="int"/> elements, whose count is always backed, so the
/// engine's own entry takes the direct path; the pooled cell drives the same element codec into the
/// same rented buffer the engine's other path uses, so the difference between the two is what the
/// pooled path costs when a count is not backed. Explains the array rows of SCALE-01 and the read
/// side of ALLOC-18.
/// </remarks>
[MemoryDiagnoser]
public class ArrayMaterializationBenchmarks
{
    private OperationState _operation;
    private byte[] _payload = [];

    [Params(16, 4096, 1_000_000)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.UnboundedTotals();

        int[] values = [.. Enumerable.Range(0, Length)];
        _payload = ComponentFixtures.Encode(ref _operation, (ref WireWriter writer) =>
        {
            writer.WriteCount(values.Length, CountKind.Array, "Array length", nullFolded: false);
            foreach (var value in values)
            {
                writer.WriteInt32(value);
            }
        });
    }

    [Benchmark(Description = "MICRO-18 read into the final array")]
    public int[] Direct()
    {
        var reader = new WireReader(_payload, ref _operation);
        var count = reader.ReadCount(CountKind.Array, "Array length", nullFolded: false);
        return Elements.ReadArray<int>(ref reader, count);
    }

    [Benchmark(Description = "MICRO-18 read through the pooled path")]
    public int[] Pooled()
    {
        var reader = new WireReader(_payload, ref _operation);
        var count = reader.ReadCount(CountKind.Array, "Array length", nullFolded: false);
        var codec = FormatterCache<int>.Instance;

        var buffer = new ElementBuffer<int>(count.CapacityHint);
        try
        {
            for (var index = 0; index < count.Value; index++)
            {
                buffer.Add(codec.Read(ref reader));
            }

            return buffer.ToArray();
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
