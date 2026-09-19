namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>Two slots for the same string instance, to show that strings are never framed.</summary>
public class SharedStrings
{
    public string? A { get; set; }

    public string? B { get; set; }
}

/// <summary>Two slots for the same array, which is registered only once it is complete.</summary>
public class SharedArrays
{
    public int[]? A { get; set; }

    public int[]? B { get; set; }
}

/// <summary>Two slots for the same immutable list.</summary>
public class SharedImmutable
{
    public System.Collections.Immutable.ImmutableList<int>? A { get; set; }

    public System.Collections.Immutable.ImmutableList<int>? B { get; set; }
}

/// <summary>Two slots for the same tuple instance.</summary>
public class SharedTuples
{
    public Tuple<int, string>? A { get; set; }

    public Tuple<int, string>? B { get; set; }
}

/// <summary>A value-typed member, which no reference frame may ever precede.</summary>
public class StructHolder
{
    public PointStruct Point { get; set; }
}

/// <summary>An object reachable through a mutable collection that holds it.</summary>
public class Holder
{
    public string? Name { get; set; }

    public List<Holder>? Items { get; set; }
}

/// <summary>An object that closes a cycle through an array, which cannot exist before its elements.</summary>
public class ArrayHolder
{
    public ArrayHolder[]? Items { get; set; }
}

/// <summary>An object that closes a cycle through a dictionary.</summary>
public class DictionaryHolder
{
    public string? Name { get; set; }

    public Dictionary<string, DictionaryHolder>? Entries { get; set; }
}

/// <summary>A struct that carries the reference which closes a cycle.</summary>
public struct HolderBox
{
    public BoxedCycle? Inner { get; set; }
}

/// <summary>An object whose cycle passes through a struct member.</summary>
public class BoxedCycle
{
    public string? Name { get; set; }

    public HolderBox Box { get; set; }
}

/// <summary>
/// A type whose <see cref="Equals(object?)"/> treats two distinct instances as equal, so a reader
/// that merged by equality rather than by identity would collapse them.
/// </summary>
public class ValueEqualNode
{
    public int Value { get; set; }

    public override bool Equals(object? obj) => obj is ValueEqualNode other && other.Value == Value;

    public override int GetHashCode() => Value;
}

/// <summary>Two slots that may hold equal — or identical — <see cref="ValueEqualNode"/> instances.</summary>
public class EqualNodePair
{
    public ValueEqualNode? A { get; set; }

    public ValueEqualNode? B { get; set; }
}
