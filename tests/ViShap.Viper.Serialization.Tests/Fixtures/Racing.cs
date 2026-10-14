using ViShap.Viper.Contracts;

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

/// <summary>The type whose codec is first used, then used again, to compare the two uses.</summary>
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

/// <summary>A declared type whose union map is invalid, because two of its arms share a tag.</summary>
[BinaryUnion(1, typeof(RacedInvalidArmA))]
[BinaryUnion(1, typeof(RacedInvalidArmB))]
public class RacedInvalidUnion
{
    public int Z { get; set; }
}

/// <summary>The first arm of <see cref="RacedInvalidUnion"/>.</summary>
public sealed class RacedInvalidArmA : RacedInvalidUnion;

/// <summary>The second arm of <see cref="RacedInvalidUnion"/>, under the same tag as the first.</summary>
public sealed class RacedInvalidArmB : RacedInvalidUnion;

/// <summary>
/// The type whose codec meets several configurations at once: reflection describes it as positional,
/// <see cref="RacedKeyedContext"/> as keyed.
/// </summary>
public sealed class RacedConfigured
{
    public int A { get; set; }
    public string? B { get; set; }
}

/// <summary>A context that describes <see cref="RacedConfigured"/> under the keyed layout.</summary>
public sealed class RacedKeyedContext : BinarySerializerContext
{
    public RacedKeyedContext() => Add(new KeyedContract());

    private sealed class KeyedContract() : TypeContract<RacedConfigured>(
        MemberLayout.Keyed,
        [new("A", typeof(int), 1), new("B", typeof(string), 2)],
        canBeConstructed: true)
    {
        public override RacedConfigured Create() => new();

        public override void Write(ref MemberWriter writer, in RacedConfigured value)
        {
            writer.Member(1, value.A);
            writer.Member(2, value.B);
        }

        public override void ReadPositional(ref MemberReader reader, ref RacedConfigured value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref RacedConfigured value)
        {
            switch (key)
            {
                case 1: value.A = reader.Member<int>(); return true;
                case 2: value.B = reader.Member<string>(); return true;
                default: return false;
            }
        }
    }
}

/// <summary>A context that holds no contract, so every type it meets is described by reflection.</summary>
public sealed class RacedEmptyContext : BinarySerializerContext;
