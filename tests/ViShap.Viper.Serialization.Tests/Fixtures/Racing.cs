namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Types reserved for the concurrency suite. Every cache in the engine is keyed by <see cref="Type"/>
/// and lives for the process, so a test about a <em>first</em> touch is only about a first touch if
/// nothing else in the suite has ever named the type. These are named here, and nowhere else.
/// </summary>
public sealed class RacedElement
{
    public int Value { get; set; }
}

/// <summary>A key for the racing dictionary and set caches; value semantics, so it can be hashed.</summary>
public readonly record struct RacedKey(int Value);

/// <summary>The type the racing activator builds.</summary>
public sealed class RacedActivated
{
    public int Value { get; set; }
}

/// <summary>The type whose member plan is built under contention.</summary>
public sealed class RacedPlan
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public List<int> Scores { get; set; } = [];
}

/// <summary>The declared type whose union map is built under contention.</summary>
[BinaryUnion(1, typeof(RacedUnionDerived))]
public class RacedUnionBase
{
    public int Z { get; set; }
}

/// <summary>The derived arm of <see cref="RacedUnionBase"/>.</summary>
public sealed class RacedUnionDerived : RacedUnionBase
{
    public int A { get; set; }
}

/// <summary>
/// The type whose contract is first built while an operation is failing on a limit, so a later
/// operation under a looser policy proves the cache kept no part of the first one's budget.
/// </summary>
public sealed class RacedBudgetType
{
    public List<int> Items { get; set; } = [];
}
