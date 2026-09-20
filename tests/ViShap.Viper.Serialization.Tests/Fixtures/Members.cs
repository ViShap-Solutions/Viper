namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// What the positional member plan includes and excludes. Only <see cref="Property"/> and
/// <see cref="Field"/> are eligible; everything else is either unwritable, non-public, an indexer or
/// a compiler-generated backing field.
/// </summary>
public class MemberSelection
{
    private int _hidden = 444;

    public int Property { get; set; }

    public int Field;

    public int GetOnly { get; } = 111;

    public int Computed => 222;

    public readonly int ReadOnlyField = 333;

    public int Hidden => _hidden;

    public int this[int index]
    {
        get => _hidden + index;
        set => _hidden = value;
    }
}

/// <summary>A member excluded by <c>[BinaryIgnore]</c>, as a property and as a field.</summary>
public class IgnoredMembers
{
    public int Kept { get; set; }

    [BinaryIgnore] public int DroppedProperty { get; set; }

    [BinaryIgnore] public int DroppedField;
}

/// <summary>
/// Non-public members pulled in by <c>[BinaryInclude]</c>. The public accessors carry
/// <c>[BinaryIgnore]</c> so the test can observe the private state without serializing it twice.
/// </summary>
public class IncludedNonPublicMembers
{
    [BinaryInclude] private int _field;

    [BinaryInclude] private string? PrivateProperty { get; set; }

    [BinaryIgnore]
    public int FieldValue
    {
        get => _field;
        set => _field = value;
    }

    [BinaryIgnore]
    public string? PropertyValue
    {
        get => PrivateProperty;
        set => PrivateProperty = value;
    }
}

/// <summary>
/// Explicit order: ordered members first, ascending, then the rest in ordinal name order.
/// Declaration order is deliberately the opposite of the plan order.
/// </summary>
public class ExplicitOrder
{
    public int Unordered { get; set; }

    [BinaryOrder(2)] public int Second { get; set; }

    public int Another { get; set; }

    [BinaryOrder(1)] public int First { get; set; }
}

/// <summary>
/// No explicit order at all. The names are chosen so that ordinal order (<c>Bravo</c>,
/// <c>Charlie</c>, <c>alpha</c>) differs from the culture-aware order a comparer without
/// <see cref="StringComparer.Ordinal"/> would produce.
/// </summary>
public class OrdinalOrder
{
    public int Charlie { get; set; }

    public int alpha { get; set; }

    public int Bravo;
}

/// <summary>Two members claiming the same explicit order.</summary>
public class DuplicateOrder
{
    [BinaryOrder(1)] public int First { get; set; }

    [BinaryOrder(1)] public int Second { get; set; }
}

/// <summary><c>[BinaryKey]</c> outside a contract type.</summary>
public class StrayKey
{
    [BinaryKey(1)] public int Value { get; set; }
}

/// <summary>A valid member followed by a contradictory one, to show when the plan is built.</summary>
public class LateContradiction
{
    public string? Early { get; set; }

    [BinaryInclude, BinaryIgnore] public int Late { get; set; }
}

/// <summary>A member-encoded type the reader cannot construct.</summary>
public class RequiresArguments
{
    public RequiresArguments(int value) => Value = value;

    public int Value { get; set; }
}

/// <summary>A struct whose members are reference types.</summary>
public struct StructWithReferences
{
    public string? Name { get; set; }

    public List<int>? Values { get; set; }

    public Node? Node { get; set; }
}

/// <summary>One leaf of <see cref="Catalogue"/>: a scalar, a time value and an array.</summary>
public class Entry
{
    public string? Title { get; set; }

    public DateTime Stamp { get; set; }

    public int[]? Scores { get; set; }
}

/// <summary>
/// A member-encoded graph that crosses the map, sequence, composite and scalar shapes before it
/// reaches another member-encoded type.
/// </summary>
public class Catalogue
{
    public Dictionary<string, List<Entry>>? Sections { get; set; }

    public (int Rank, string Label)[]? Ranked { get; set; }

    public Entry? Featured { get; set; }
}

/// <summary>A concrete collection no dedicated formatter claims, reached by the last-resort shape.</summary>
public class Bag : ICollection<int>
{
    private readonly List<int> _items = [];

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public void Add(int item) => _items.Add(item);

    public void Clear() => _items.Clear();

    public bool Contains(int item) => _items.Contains(item);

    public void CopyTo(int[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public bool Remove(int item) => _items.Remove(item);

    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        _items.GetEnumerator();
}

/// <summary>An abstract type with no union map, which the reader cannot construct.</summary>
public abstract class AbstractPerson
{
    public string? Name { get; set; }
}
