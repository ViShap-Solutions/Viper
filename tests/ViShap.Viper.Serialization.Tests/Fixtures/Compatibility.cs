using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// The shapes the committed <c>Fixtures/Wire/v1-*.bin</c> and <c>v0-*.bin</c> files decode into.
/// </summary>
/// <remarks>
/// These types are frozen. Their bytes were produced by the v1.0.0 writer and committed, so the
/// member set, the member order and the keys below are part of the released wire format: renaming a
/// member, reordering one, adding one or changing a type invalidates a fixture and is a
/// compatibility break, not a refactoring. Positional members carry an explicit
/// <c>[BinaryOrder]</c> so a rename cannot silently reorder the layout.
/// <para>
/// <c>TimeZoneInfo</c> is deliberately absent: it travels as <c>ToSerializedString()</c>, which
/// depends on the host's time-zone database, so a committed fixture would decode differently on a
/// different machine and would be testing the operating system rather than the format.
/// </para>
/// </remarks>
public sealed class FrozenPrimitives
{
    [BinaryOrder(1)] public bool Flag { get; set; }
    [BinaryOrder(2)] public byte Byte { get; set; }
    [BinaryOrder(3)] public sbyte SByte { get; set; }
    [BinaryOrder(4)] public short Short { get; set; }
    [BinaryOrder(5)] public ushort UShort { get; set; }
    [BinaryOrder(6)] public int Int { get; set; }
    [BinaryOrder(7)] public uint UInt { get; set; }
    [BinaryOrder(8)] public long Long { get; set; }
    [BinaryOrder(9)] public ulong ULong { get; set; }
    [BinaryOrder(10)] public float Float { get; set; }
    [BinaryOrder(11)] public double Double { get; set; }
    [BinaryOrder(12)] public decimal Decimal { get; set; }
    [BinaryOrder(13)] public char Char { get; set; }
    [BinaryOrder(14)] public string Text { get; set; } = string.Empty;
    [BinaryOrder(15)] public FrozenChoice Choice { get; set; }
    [BinaryOrder(16)] public Half Half { get; set; }
    [BinaryOrder(17)] public Int128 Int128 { get; set; }
    [BinaryOrder(18)] public UInt128 UInt128 { get; set; }
    [BinaryOrder(19)] public nint NativeInt { get; set; }
    [BinaryOrder(20)] public nuint NativeUInt { get; set; }
    [BinaryOrder(21)] public Rune Rune { get; set; }
    [BinaryOrder(22)] public BigInteger BigInteger { get; set; }
}

/// <summary>The enum a frozen fixture carries; its numeric values are part of the fixture.</summary>
public enum FrozenChoice
{
    /// <summary>Value 0.</summary>
    First = 0,

    /// <summary>Value 7, so a fixture proves the underlying number travels rather than the name.</summary>
    Seventh = 7
}

/// <summary>The time and system families of a frozen fixture.</summary>
public sealed class FrozenTimeAndSystem
{
    [BinaryOrder(1)] public DateTime Timestamp { get; set; }
    [BinaryOrder(2)] public DateTimeOffset Offset { get; set; }
    [BinaryOrder(3)] public TimeSpan Elapsed { get; set; }
    [BinaryOrder(4)] public DateOnly Date { get; set; }
    [BinaryOrder(5)] public TimeOnly Time { get; set; }
    [BinaryOrder(6)] public Guid Id { get; set; }
    [BinaryOrder(7)] public Uri? Location { get; set; }
    [BinaryOrder(8)] public Version? Release { get; set; }
    [BinaryOrder(9)] public StringBuilder? Builder { get; set; }
    [BinaryOrder(10)] public CultureInfo? Culture { get; set; }
    [BinaryOrder(11)] public BitArray? Bits { get; set; }
}

/// <summary>The numerics family of a frozen fixture.</summary>
public sealed class FrozenNumerics
{
    [BinaryOrder(1)] public Complex Complex { get; set; }
    [BinaryOrder(2)] public Vector2 Vector2 { get; set; }
    [BinaryOrder(3)] public Vector3 Vector3 { get; set; }
    [BinaryOrder(4)] public Vector4 Vector4 { get; set; }
    [BinaryOrder(5)] public Quaternion Quaternion { get; set; }
    [BinaryOrder(6)] public Plane Plane { get; set; }
    [BinaryOrder(7)] public Matrix3x2 Matrix3x2 { get; set; }
    [BinaryOrder(8)] public Matrix4x4 Matrix4x4 { get; set; }
}

/// <summary>The container families of a frozen fixture, one representative of each shape.</summary>
public sealed class FrozenCollections
{
    [BinaryOrder(1)] public int[]? Array { get; set; }
    [BinaryOrder(2)] public List<string>? List { get; set; }
    [BinaryOrder(3)] public Dictionary<string, int>? Dictionary { get; set; }
    [BinaryOrder(4)] public SortedDictionary<int, string>? Sorted { get; set; }
    [BinaryOrder(5)] public HashSet<int>? Set { get; set; }
    [BinaryOrder(6)] public Stack<int>? Stack { get; set; }
    [BinaryOrder(7)] public Queue<int>? Queue { get; set; }
    [BinaryOrder(8)] public LinkedList<int>? Linked { get; set; }
    [BinaryOrder(9)] public ImmutableList<int> Immutable { get; set; } = ImmutableList<int>.Empty;
    [BinaryOrder(10)] public ImmutableArray<int> ImmutableArray { get; set; }
}

/// <summary>The composite family of a frozen fixture.</summary>
public sealed class FrozenComposites
{
    [BinaryOrder(1)] public KeyValuePair<string, int> Pair { get; set; }
    [BinaryOrder(2)] public (int Number, string Text) ValueTuple { get; set; }
    [BinaryOrder(3)] public Tuple<int, string>? Tuple { get; set; }
    [BinaryOrder(4)] public Lazy<int>? Lazy { get; set; }
    [BinaryOrder(5)] public int[,]? Grid { get; set; }
    [BinaryOrder(6)] public ArraySegment<int> Segment { get; set; }
    [BinaryOrder(7)] public Memory<int> Memory { get; set; }
}

/// <summary>A frozen keyed contract; its keys are the wire format and outlive any rename.</summary>
[BinaryContract]
public sealed class FrozenKeyed
{
    [BinaryKey(1)] public int Number { get; set; }
    [BinaryKey(2)] public string? Text { get; set; }
    [BinaryKey(7)] public List<int>? Numbers { get; set; }
}

/// <summary>The base of a frozen union; the tags are the wire format.</summary>
[BinaryUnion(1, typeof(FrozenCircle))]
[BinaryUnion(2, typeof(FrozenSquare))]
public abstract class FrozenShape
{
    /// <summary>A member every arm carries.</summary>
    [BinaryOrder(1)] public string Label { get; set; } = string.Empty;
}

/// <summary>Union tag 1.</summary>
public sealed class FrozenCircle : FrozenShape
{
    [BinaryOrder(2)] public double Radius { get; set; }
}

/// <summary>Union tag 2.</summary>
public sealed class FrozenSquare : FrozenShape
{
    [BinaryOrder(2)] public double Side { get; set; }
}

/// <summary>A frozen graph with a shared node and a cycle, written under preserved references.</summary>
public sealed class FrozenGraph
{
    [BinaryOrder(1)] public FrozenNode? Left { get; set; }
    [BinaryOrder(2)] public FrozenNode? Right { get; set; }
}

/// <summary>A node of <see cref="FrozenGraph"/>; <see cref="Next"/> may close a cycle.</summary>
public sealed class FrozenNode
{
    [BinaryOrder(1)] public int Value { get; set; }
    [BinaryOrder(2)] public FrozenNode? Next { get; set; }
}

/// <summary>Nulls and empty containers, which have their own framing.</summary>
public sealed class FrozenAbsences
{
    [BinaryOrder(1)] public string? NullText { get; set; }
    [BinaryOrder(2)] public int? NullNumber { get; set; }
    [BinaryOrder(3)] public int? PresentNumber { get; set; }
    [BinaryOrder(4)] public List<int>? NullList { get; set; }
    [BinaryOrder(5)] public List<int>? EmptyList { get; set; }
    [BinaryOrder(6)] public int[]? EmptyArray { get; set; }
    [BinaryOrder(7)] public Dictionary<int, int>? EmptyDictionary { get; set; }
    [BinaryOrder(8)] public string? EmptyText { get; set; }
    [BinaryOrder(9)] public ImmutableArray<int> DefaultImmutableArray { get; set; }
}
