using BenchmarkDotNet.Attributes;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Io;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-02 — what a validated count costs: the limit comparison and the element charge that
/// <see cref="ElementCount.Validate"/> performs, plus the two other charges the engine makes per node
/// and per keyed field.
/// </summary>
/// <remarks>
/// Explains DIFF-05 and the B-P2 column of the profile matrix: a tight limit policy changes these
/// comparisons and nothing else, so the accounting cost of a payload is this cell times the number of
/// containers, nodes and keyed fields it holds. One invocation is a thousand charges.
/// <para>
/// The counters grow for as long as the run lasts, and that is deliberate: a charge is one comparison
/// against the ceiling, whose cost does not depend on how much the operation has already consumed, so
/// a later iteration observes nothing an earlier one left behind (VAL-04).
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class BudgetBenchmarks
{
    private const int Operations = 1_000;

    /// <summary>
    /// The shape validated by the multi-dimensional path: a rank-3 array, which is the only count that
    /// costs more than one comparison.
    /// </summary>
    private static readonly int[] Shape = [16, 16, 16];

    private SerializationOperation _operation = null!;

    [GlobalSetup]
    public void Setup() => _operation = ComponentFixtures.UnboundedTotals();

    [Benchmark(Description = "MICRO-02 validate collection count", OperationsPerInvoke = Operations)]
    public long ValidateCollectionCount()
    {
        long total = 0;

        for (var i = 0; i < Operations; i++)
        {
            total += ElementCount.Validate(8, CountKind.Collection, _operation, "MICRO-02");
        }

        return total;
    }

    [Benchmark(Description = "MICRO-02 validate array count", OperationsPerInvoke = Operations)]
    public long ValidateArrayCount()
    {
        long total = 0;

        for (var i = 0; i < Operations; i++)
        {
            total += ElementCount.Validate(8, CountKind.Array, _operation, "MICRO-02");
        }

        return total;
    }

    [Benchmark(Description = "MICRO-02 validate dictionary count", OperationsPerInvoke = Operations)]
    public long ValidateDictionaryCount()
    {
        long total = 0;

        for (var i = 0; i < Operations; i++)
        {
            total += ElementCount.Validate(8, CountKind.Dictionary, _operation, "MICRO-02");
        }

        return total;
    }

    [Benchmark(Description = "MICRO-02 validate rank-3 shape", OperationsPerInvoke = Operations)]
    public long ValidateShape()
    {
        long total = 0;

        for (var i = 0; i < Operations; i++)
        {
            total += ElementCount.ValidateShape(Shape, _operation, "MICRO-02");
        }

        return total;
    }

    [Benchmark(Description = "MICRO-02 charge graph node", OperationsPerInvoke = Operations)]
    public long ChargeGraphNode()
    {
        var budget = _operation.Budget;

        for (var i = 0; i < Operations; i++)
        {
            budget.ConsumeObjectGraphNodes(1);
        }

        return budget.ObjectGraphNodes;
    }

    [Benchmark(Description = "MICRO-02 charge keyed field", OperationsPerInvoke = Operations)]
    public long ChargeKeyedField()
    {
        var budget = _operation.Budget;

        for (var i = 0; i < Operations; i++)
        {
            budget.ConsumeKeyedFields(1);
        }

        return budget.KeyedFields;
    }
}

/// <summary>
/// MICRO-03 — the depth scope: entering and leaving one structural level, descending a chain of them,
/// and unwinding that chain when a level throws.
/// </summary>
/// <remarks>
/// Explains SCALE-03: the depth curve is this scope repeated once per level, so a curve steeper than
/// this cell times the level count is a cost somewhere other than depth accounting. The unwind column
/// is what a limit breach costs after the fact, which is the path a hostile payload takes.
/// </remarks>
[MemoryDiagnoser]
public class DepthScopeBenchmarks
{
    private const int Operations = 1_000;

    private SerializationOperation _operation = null!;

    /// <summary>Levels per descent, up to just below the default ceiling of 512.</summary>
    [Params(1, 8, 64, 500)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup() => _operation = ComponentFixtures.UnboundedTotals();

    [Benchmark(Description = "MICRO-03 enter and exit", OperationsPerInvoke = Operations)]
    public int EnterAndExit()
    {
        var budget = _operation.Budget;
        var reached = 0;

        for (var i = 0; i < Operations; i++)
        {
            using var scope = budget.EnterDepth();
            reached = budget.Depth;
        }

        return reached;
    }

    [Benchmark(Description = "MICRO-03 descend and return")]
    public int Descend() => Descend(_operation.Budget, Depth);

    [Benchmark(Description = "MICRO-03 descend and unwind")]
    public int Unwind()
    {
        var budget = _operation.Budget;

        try
        {
            DescendAndThrow(budget, Depth);
        }
        catch (BinaryFormatException)
        {
            // The depth the scopes restored is the result: an unwind that leaked a level would show
            // here as a non-zero depth rather than as a wrong timing.
        }

        return budget.Depth;
    }

    private static int Descend(SerializationBudget budget, int remaining)
    {
        using var scope = budget.EnterDepth();
        return remaining == 0 ? budget.Depth : Descend(budget, remaining - 1);
    }

    private static void DescendAndThrow(SerializationBudget budget, int remaining)
    {
        using var scope = budget.EnterDepth();

        if (remaining == 0)
        {
            throw new BinaryFormatException("MICRO-03 unwind.");
        }

        DescendAndThrow(budget, remaining - 1);
    }
}
