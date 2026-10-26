using BenchmarkDotNet.Attributes;
using ViShap.Viper.Engine;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models.Viper;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-07 — reference identity at four sharing densities, in both directions: the lookup-then-register
/// step the writer performs for every structural reference-typed value, and the register-then-resolve
/// step the reader performs for the same value.
/// </summary>
/// <remarks>
/// One operation takes one table from the per-thread pool and hands it back cleared, so each
/// invocation rents a table and returns it, as one serialization does. Renting and returning are
/// therefore inside the timed region, and the third cell of this class is that pair alone, amortized
/// over the same thousand operations, so it subtracts directly from the other two.
/// <para>
/// Explains SCALE-07 and DIFF-04, where the same densities are measured end to end through B-P1.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ReferenceIdentityBenchmarks
{
    private const int Operations = 1_000;

    private object[] _values = [];
    private int[] _ids = [];

    /// <summary>The share of values already registered when the table is asked about them.</summary>
    [Params(0, 10, 50, 90)]
    public int SharedPercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new DeterministicRandom(0x0000_1807UL + (ulong)SharedPercent);
        var shared = new DagNode { Id = -1, Name = "shared", Weight = 1 };

        _values = new object[Operations];
        _ids = new int[Operations];

        for (var index = 0; index < Operations; index++)
        {
            _values[index] = random.Next(100) < SharedPercent
                ? shared
                : new DagNode { Id = index, Name = "node", Weight = index };
        }

        // The read side meets the ids the write side produced, in the same order.
        var table = WriteReferenceTable.Rent();

        for (var index = 0; index < Operations; index++)
        {
            _ids[index] = table.TryGetVisibleId(_values[index], out int id) ? id : table.Register(_values[index]);
        }

        WriteReferenceTable.Return(table);
    }

    [Benchmark(Description = "MICRO-07 write lookup and register", OperationsPerInvoke = Operations)]
    public int WriteIdentity()
    {
        var table = WriteReferenceTable.Rent();
        var backReferences = 0;

        for (var index = 0; index < Operations; index++)
        {
            if (table.TryGetVisibleId(_values[index], out _))
            {
                backReferences++;
            }
            else
            {
                _ = table.Register(_values[index]);
            }
        }

        WriteReferenceTable.Return(table);
        return backReferences;
    }

    [Benchmark(Description = "MICRO-07 read register and resolve", OperationsPerInvoke = Operations)]
    public int ReadIdentity()
    {
        var table = ReadReferenceTable.Rent();
        var resolved = 0;

        for (var index = 0; index < Operations; index++)
        {
            if (table.TryResolve(_ids[index], out _))
            {
                resolved++;
            }
            else
            {
                table.Register(_ids[index], _values[index]);
            }
        }

        ReadReferenceTable.Return(table);
        return resolved;
    }

    [Benchmark(Description = "MICRO-07 table rent and return", OperationsPerInvoke = Operations)]
    public object TableRentAndReturn()
    {
        var table = WriteReferenceTable.Rent();
        WriteReferenceTable.Return(table);
        return table;
    }
}

/// <summary>
/// MICRO-07 — the reference scope itself: entering one and leaving it, which is what a keyed field
/// costs in ancestor-chain visibility whether or not it holds a reference.
/// </summary>
/// <remarks>
/// The table survives the whole run here because a balanced enter and exit leaves nothing behind.
/// Explains the reference-framing part of every keyed B-P1 cell.
/// </remarks>
[MemoryDiagnoser]
public class ReferenceScopeBenchmarks
{
    private const int Operations = 1_000;

    private WriteReferenceTable _writeTable = null!;
    private ReadReferenceTable _readTable = null!;

    [GlobalSetup]
    public void Setup()
    {
        _writeTable = WriteReferenceTable.Rent();
        _readTable = ReadReferenceTable.Rent();
    }

    [Benchmark(Description = "MICRO-07 write scope enter and exit", OperationsPerInvoke = Operations)]
    public int WriteScope()
    {
        var entered = 0;

        for (var index = 0; index < Operations; index++)
        {
            using var scope = _writeTable.Enter();
            entered++;
        }

        return entered;
    }

    [Benchmark(Description = "MICRO-07 read scope enter and exit", OperationsPerInvoke = Operations)]
    public int ReadScope()
    {
        var entered = 0;

        for (var index = 0; index < Operations; index++)
        {
            using var scope = _readTable.Enter();
            entered++;
        }

        return entered;
    }
}
