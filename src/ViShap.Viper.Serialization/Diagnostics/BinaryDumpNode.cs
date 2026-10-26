namespace ViShap.Viper.Diagnostics;

/// <summary>What a node of a <see cref="BinaryDump"/> tree stands for on the wire.</summary>
public enum BinaryDumpNodeKind
{
    /// <summary>A value written as null.</summary>
    Null,

    /// <summary>A self-contained value: a number, a string, a time, a GUID and the like.</summary>
    Scalar,

    /// <summary>An array, a list, a set or another sequence; its children are the elements.</summary>
    Sequence,

    /// <summary>A dictionary or another map; its children are the keys and the values, in order.</summary>
    Map,

    /// <summary>An object whose members are written in plan order; its children are the members.</summary>
    Object,

    /// <summary>An object of a <see cref="BinaryContractAttribute"/> type; its children are its keyed fields.</summary>
    KeyedObject,

    /// <summary>A keyed field the reading type knows; its child is the member's value.</summary>
    KeyedField,

    /// <summary>A keyed field the reading type does not know, skipped by its declared length.</summary>
    UnknownKeyedField,

    /// <summary>A value of a type with a <see cref="BinaryUnionAttribute"/> map; it carries the union tag and its runtime type.</summary>
    Union,

    /// <summary>A reference to an object that appeared earlier in the payload; it ends the value.</summary>
    BackReference,

    /// <summary>A tuple, a key/value pair, a lazy value or an array of rank greater than one.</summary>
    Composite
}

/// <summary>One value of a payload as a <see cref="BinaryDump"/> read it, with where it lies on the wire.</summary>
public sealed class BinaryDumpNode
{
    internal BinaryDumpNode(string name, string typeName, long offset)
    {
        Name = name;
        TypeName = typeName;
        Offset = offset;
    }

    /// <summary>The member name, <c>[index]</c> for an element, <c>{key}</c> for a map value, or the type name for the root.</summary>
    public string Name { get; internal set; }

    /// <summary>The declared type of the value; for a union, the runtime type its tag names.</summary>
    public string TypeName { get; internal set; }

    /// <summary>What the node stands for on the wire.</summary>
    public BinaryDumpNodeKind Kind { get; internal set; } = BinaryDumpNodeKind.Scalar;

    /// <summary>The offset of the node's first byte in the payload.</summary>
    public long Offset { get; }

    /// <summary>The bytes the node occupies on the wire, its framing and its children included.</summary>
    public int Length { get; internal set; }

    /// <summary>
    /// A scalar value rendered with the invariant culture, times in UTC; a string is cut at 256
    /// characters. <see langword="null"/> for a node that is not a scalar.
    /// </summary>
    public string? Value { get; internal set; }

    /// <summary>The key of a keyed field; otherwise <see langword="null"/>.</summary>
    public int? Key { get; internal set; }

    /// <summary>The union tag of a union value; otherwise <see langword="null"/>.</summary>
    public byte? UnionTag { get; internal set; }

    /// <summary>The object id a reference frame gave the value, when the payload preserves references.</summary>
    public int? ReferenceId { get; internal set; }

    /// <summary>For a back reference, the path of the value it points to.</summary>
    public string? ReferenceTarget { get; internal set; }

    /// <summary>The count a sequence, a map, an object or a keyed object declared.</summary>
    internal int? DeclaredCount { get; set; }

    /// <summary>The node's children, in wire order.</summary>
    public IReadOnlyList<BinaryDumpNode> Children => ChildList;

    internal List<BinaryDumpNode> ChildList { get; } = [];
}

/// <summary>The first node at which two dumps of the same type differ, as <see cref="BinaryFormatDumper.Compare{T}(ReadOnlySpan{byte}, ReadOnlySpan{byte}, BinarySerializerOptions?)"/> reports it.</summary>
public sealed class BinaryDumpDifference
{
    internal BinaryDumpDifference(string path, BinaryDumpNode? expected, BinaryDumpNode? actual)
    {
        Path = path;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The path of the node that differs, such as <c>Order.Lines[2].Note</c>.</summary>
    public string Path { get; }

    /// <summary>The node in the expected frame; <see langword="null"/> when only the actual frame has it.</summary>
    public BinaryDumpNode? Expected { get; }

    /// <summary>The node in the actual frame; <see langword="null"/> when only the expected frame has it.</summary>
    public BinaryDumpNode? Actual { get; }
}
