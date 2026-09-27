using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// ALLOC-09 — cycle detection without references (Contract §16): one write of a chain as deep as the
/// cell, through the engine into its own buffer, with the path from the root searched for every
/// structural value. The second cell is the alternative it is measured against: a per-operation
/// <see cref="HashSet{T}"/> by reference, added to and removed from along the same path, so the two
/// cost the same path on one table.
/// </summary>
/// <remarks>
/// One operation serves every invocation, under ceilings a run cannot reach, so the node budget the
/// engine charges is the single comparison a real call makes and never runs out across invocations.
/// <para>
/// The set is a replica built in this project, not Viper code; it gives the ancestor stack a
/// like-for-like comparison at depths <c>Baselines/pre-rework/</c> does not hold. The depth-500
/// engine cell also compares with the SCALE-03 serialize cell of that baseline, which writes the same
/// kind of chain end to end.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CycleDetectionBenchmarks
{
    private SerializationOperation _operation = null!;
    private DeepNode _root = new();
    private object[] _path = [];

    [Params(4, 32, 500)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.UnboundedTotals();

        DeepNode? child = null;
        var path = new List<object>();

        for (var level = Depth; level >= 1; level--)
        {
            child = new DeepNode { Level = level, Label = "node", Payload = level, Child = child };
            path.Add(child);
        }

        _root = child!;
        path.Reverse();
        _path = [.. path];
    }

    [Benchmark(Description = "ALLOC-09 engine write, ancestor stack")]
    public long EngineWrite()
    {
        using var buffer = new PayloadBuffer(_operation.Limits.MaxPayloadBytes, "payload");
        var writer = new WireWriter(buffer, _operation);

        using (var engine = new GraphWriter(_operation))
        {
            engine.WriteRoot(ref writer, _root);
        }

        writer.Flush();
        return buffer.Length;
    }

    [Benchmark(Description = "ALLOC-09 per-operation HashSet replica")]
    public int HashSetReplica()
    {
        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var value in _path)
        {
            if (!active.Add(value))
            {
                throw new InvalidOperationException("The replica met a cycle in an acyclic chain.");
            }
        }

        for (var index = _path.Length - 1; index >= 0; index--)
        {
            active.Remove(_path[index]);
        }

        return active.Count;
    }
}
