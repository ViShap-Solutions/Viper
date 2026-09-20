using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-04 — the member plan once it is cached: what the reader and the writer pay per value to
/// obtain the description of a concrete type, positional and keyed, and for a union map.
/// </summary>
/// <remarks>
/// Explains the steady-state half of DIFF-03 and the first-use column of every profile row: a
/// contract is built once per type and looked up once per value, so a steady-state cell is this
/// lookup and a first-use cell is the construction the cold runner reports.
/// </remarks>
[MemoryDiagnoser]
public class ContractLookupBenchmarks
{
    private const int Operations = 1_000;

    [GlobalSetup]
    public void Setup()
    {
        // The measurement is the cached lookup, so the three contracts are built here, once.
        _ = TypeContractCache.Get(typeof(MediumObject));
        _ = TypeContractCache.Get(typeof(KeyedOrder));
        _ = TypeContractCache.GetUnion(typeof(EventBase));
    }

    [Benchmark(Description = "MICRO-04 positional lookup", OperationsPerInvoke = Operations)]
    public int PositionalLookup()
    {
        var members = 0;

        for (var i = 0; i < Operations; i++)
        {
            members += TypeContractCache.Get(typeof(MediumObject)).Members.Length;
        }

        return members;
    }

    [Benchmark(Description = "MICRO-04 keyed lookup", OperationsPerInvoke = Operations)]
    public int KeyedLookup()
    {
        var members = 0;

        for (var i = 0; i < Operations; i++)
        {
            members += TypeContractCache.Get(typeof(KeyedOrder)).Members.Length;
        }

        return members;
    }

    [Benchmark(Description = "MICRO-04 union lookup", OperationsPerInvoke = Operations)]
    public int UnionLookup()
    {
        var found = 0;

        for (var i = 0; i < Operations; i++)
        {
            if (TypeContractCache.GetUnion(typeof(EventBase))!.TryGetTag(typeof(TextEvent), out _))
            {
                found++;
            }
        }

        return found;
    }
}

/// <summary>
/// MICRO-05 — formatter resolution: a type a formatter claims, and a type none claims, which is how
/// the engine learns that a value is member-encoded.
/// </summary>
/// <remarks>
/// Explains why a member-encoded value carries no resolution penalty over a claimed one: both answers
/// come from the same cache, and a null answer is cached like any other. Every WL-01 cell pays this
/// once per value.
/// </remarks>
[MemoryDiagnoser]
public class FormatterResolutionBenchmarks
{
    private const int Operations = 1_000;

    [GlobalSetup]
    public void Setup()
    {
        _ = FormatterRegistry.Resolve(typeof(List<TinyFlat>));
        _ = FormatterRegistry.Resolve(typeof(Dictionary<string, ScalarRecord>));
        _ = FormatterRegistry.Resolve(typeof(MediumObject));
    }

    [Benchmark(Description = "MICRO-05 resolve claimed sequence", OperationsPerInvoke = Operations)]
    public int ResolveSequence()
    {
        var resolved = 0;

        for (var i = 0; i < Operations; i++)
        {
            if (FormatterRegistry.Resolve(typeof(List<TinyFlat>)) is not null)
            {
                resolved++;
            }
        }

        return resolved;
    }

    [Benchmark(Description = "MICRO-05 resolve claimed map", OperationsPerInvoke = Operations)]
    public int ResolveMap()
    {
        var resolved = 0;

        for (var i = 0; i < Operations; i++)
        {
            if (FormatterRegistry.Resolve(typeof(Dictionary<string, ScalarRecord>)) is not null)
            {
                resolved++;
            }
        }

        return resolved;
    }

    [Benchmark(Description = "MICRO-05 resolve member-encoded", OperationsPerInvoke = Operations)]
    public int ResolveMemberEncoded()
    {
        var unclaimed = 0;

        for (var i = 0; i < Operations; i++)
        {
            if (FormatterRegistry.Resolve(typeof(MediumObject)) is null)
            {
                unclaimed++;
            }
        }

        return unclaimed;
    }
}
