namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>A complete contract covering the shapes a keyed field can carry.</summary>
[BinaryContract]
public class CompleteContract
{
    [BinaryKey(1)] public int Number { get; set; }

    [BinaryKey(2)] public string? Text { get; set; }

    [BinaryKey(3)] public List<int>? Numbers { get; set; }

    [BinaryKey(4)] public Node? Child { get; set; }

    [BinaryIgnore] public int Excluded { get; set; }
}

/// <summary>The writer's schema: a field the reader below has never heard of sits between two it knows.</summary>
[BinaryContract]
public class ThreeKeys
{
    [BinaryKey(1)] public int First { get; set; }

    [BinaryKey(2)] public string? Middle { get; set; }

    [BinaryKey(3)] public int Last { get; set; }
}

/// <summary>The reader's schema for <see cref="ThreeKeys"/>: key 2 was retired.</summary>
[BinaryContract]
public class OuterKeys
{
    [BinaryKey(1)] public int First { get; set; }

    [BinaryKey(3)] public int Last { get; set; }
}

/// <summary>A writer schema whose retired field carries a nested graph rather than a scalar.</summary>
[BinaryContract]
public class NestedMiddle
{
    [BinaryKey(1)] public int First { get; set; }

    [BinaryKey(2)] public Dictionary<string, List<Node>>? Middle { get; set; }

    [BinaryKey(3)] public int Last { get; set; }
}

/// <summary>Two sibling fields that share the parent element budget.</summary>
[BinaryContract]
public class TwoLists
{
    [BinaryKey(1)] public List<int>? A { get; set; }

    [BinaryKey(2)] public List<int>? B { get; set; }
}

/// <summary>Two sibling fields that may hold the same object.</summary>
[BinaryContract]
public class TwoNodes
{
    [BinaryKey(1)] public Node? A { get; set; }

    [BinaryKey(2)] public Node? B { get; set; }
}

/// <summary>A contract that refers to itself across a keyed field boundary.</summary>
[BinaryContract]
public class KeyedCycle
{
    [BinaryKey(1)] public string? Name { get; set; }

    [BinaryKey(2)] public KeyedCycle? Self { get; set; }
}

/// <summary>A polymorphic member inside a keyed field.</summary>
[BinaryContract]
public class KeyedUnion
{
    [BinaryKey(1)] public UnionBase? Shape { get; set; }

    [BinaryKey(2)] public int Marker { get; set; }
}

/// <summary>The key values the 7-bit encoding treats differently, plus the largest one admitted.</summary>
[BinaryContract]
public class KeyBoundaries
{
    [BinaryKey(0)] public int Zero { get; set; }

    [BinaryKey(127)] public int OneByte { get; set; }

    [BinaryKey(128)] public int TwoBytes { get; set; }

    [BinaryKey(int.MaxValue)] public int Largest { get; set; }
}

/// <summary>A key below the admitted range.</summary>
[BinaryContract]
public class NegativeKey
{
    [BinaryKey(-1)] public int Value { get; set; }
}

/// <summary>Two members claiming the same key.</summary>
[BinaryContract]
public class DuplicateKeys
{
    [BinaryKey(1)] public int First { get; set; }

    [BinaryKey(1)] public int Second { get; set; }
}

/// <summary>A contract member that states neither <c>[BinaryKey]</c> nor <c>[BinaryIgnore]</c>.</summary>
[BinaryContract]
public class UnmarkedContractMember
{
    [BinaryKey(1)] public int Keyed { get; set; }

    public int Unmarked { get; set; }
}

/// <summary><c>[BinaryInclude]</c> has no meaning under a contract.</summary>
[BinaryContract]
public class ContractWithInclude
{
    [BinaryKey(1)] public int Keyed { get; set; }

    [BinaryInclude, BinaryKey(2)] private int _hidden;

    [BinaryIgnore]
    public int HiddenValue
    {
        get => _hidden;
        set => _hidden = value;
    }
}

/// <summary><c>[BinaryOrder]</c> has no meaning under a contract.</summary>
[BinaryContract]
public class ContractWithOrder
{
    [BinaryKey(1), BinaryOrder(1)] public int Keyed { get; set; }
}
