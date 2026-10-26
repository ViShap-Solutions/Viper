using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-04 — the type contract once it is cached: what the polymorphic slot pays per value to find
/// the contract of a runtime type, positional and keyed, and to find a union map.
/// </summary>
/// <remarks>
/// Explains the steady-state half of DIFF-03 and the union rows of the profile matrix. A declared
/// type's codec holds its own contract after first use, so a value whose runtime type is the declared
/// one pays no lookup at all; a value in a polymorphic slot pays this lookup once. A first-use cell
/// is the construction the cold runner reports.
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
/// MICRO-05 — codec resolution: a type a shape claims, and a type none claims, which is member-encoded
/// through its contract.
/// </summary>
/// <remarks>
/// Explains why a member-encoded value carries no resolution penalty over a claimed one: both are a
/// read of the static field of <see cref="FormatterCache{T}"/>, filled once per type. Every WL-01 cell
/// pays this once per value.
/// </remarks>
[MemoryDiagnoser]
public class FormatterResolutionBenchmarks
{
    private const int Operations = 1_000;

    [GlobalSetup]
    public void Setup()
    {
        _ = FormatterCache<List<TinyFlat>>.Instance;
        _ = FormatterCache<Dictionary<string, ScalarRecord>>.Instance;
        _ = FormatterCache<MediumObject>.Instance;
    }

    [Benchmark(Description = "MICRO-05 resolve claimed sequence", OperationsPerInvoke = Operations)]
    public int ResolveSequence()
    {
        var resolved = 0;

        for (var i = 0; i < Operations; i++)
        {
            if (FormatterCache<List<TinyFlat>>.Instance.Shape == CodecShape.Sequence)
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
            if (FormatterCache<Dictionary<string, ScalarRecord>>.Instance.Shape == CodecShape.Map)
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
            if (FormatterCache<MediumObject>.Instance.Shape == CodecShape.Object)
            {
                unclaimed++;
            }
        }

        return unclaimed;
    }
}
