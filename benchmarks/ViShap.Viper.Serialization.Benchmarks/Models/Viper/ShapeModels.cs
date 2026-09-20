using System.Collections.Immutable;
using System.Numerics;

namespace ViShap.Viper.Serialization.Benchmarks.Models.Viper;

/// <summary>DATA-10 — a node reachable through several paths, so identity is what is being measured.</summary>
internal sealed class DagNode
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Weight { get; set; }

    public List<DagNode> Children { get; set; } = [];
}

/// <summary>DATA-11 — a graph that cannot travel without reference support.</summary>
internal sealed class CyclicNode
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public CyclicNode? Parent { get; set; }

    public List<CyclicNode> Children { get; set; } = [];
}

/// <summary>DATA-12 — a base carrying a union map, so only a tag travels.</summary>
[BinaryUnion(1, typeof(TextEvent))]
[BinaryUnion(2, typeof(NumericEvent))]
[BinaryUnion(3, typeof(StateEvent))]
[BinaryUnion(4, typeof(LocationEvent))]
[BinaryUnion(5, typeof(BatchEvent))]
[BinaryUnion(6, typeof(ErrorEvent))]
internal class EventBase
{
    public long Sequence { get; set; }

    public DateTime AtUtc { get; set; }
}

internal sealed class TextEvent : EventBase
{
    public string Text { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}

internal sealed class NumericEvent : EventBase
{
    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;
}

internal sealed class StateEvent : EventBase
{
    public Severity From { get; set; }

    public Severity To { get; set; }

    public bool Automatic { get; set; }
}

internal sealed class LocationEvent : EventBase
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public float Accuracy { get; set; }
}

internal sealed class BatchEvent : EventBase
{
    public List<long> Ids { get; set; } = [];

    public int Total { get; set; }
}

internal sealed class ErrorEvent : EventBase
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int Attempt { get; set; }
}

/// <summary>DATA-13 — the writing side of a keyed contract: keys, not positions, carry the layout.</summary>
[BinaryContract]
internal sealed class KeyedOrder
{
    [BinaryKey(1)]
    public Guid Id { get; set; }

    [BinaryKey(2)]
    public string Customer { get; set; } = string.Empty;

    [BinaryKey(3)]
    public decimal Total { get; set; }

    [BinaryKey(4)]
    public DateTime PlacedUtc { get; set; }

    [BinaryKey(5)]
    public Channel Channel { get; set; }

    [BinaryKey(6)]
    public List<KeyedLine> Lines { get; set; } = [];

    [BinaryKey(7)]
    public string? Note { get; set; }

    [BinaryKey(8)]
    public int Revision { get; set; }
}

[BinaryContract]
internal sealed class KeyedLine
{
    [BinaryKey(1)]
    public string Sku { get; set; } = string.Empty;

    [BinaryKey(2)]
    public int Quantity { get; set; }

    [BinaryKey(3)]
    public decimal UnitPrice { get; set; }

    [BinaryKey(4)]
    public string? Comment { get; set; }
}

/// <summary>DATA-13 — the reading side: keys removed, added and reordered against <see cref="KeyedOrder"/>.</summary>
[BinaryContract]
internal sealed class KeyedOrderV2
{
    [BinaryKey(8)]
    public int Revision { get; set; }

    [BinaryKey(1)]
    public Guid Id { get; set; }

    [BinaryKey(3)]
    public decimal Total { get; set; }

    [BinaryKey(6)]
    public List<KeyedLine> Lines { get; set; } = [];

    [BinaryKey(9)]
    public string? Warehouse { get; set; }

    [BinaryKey(10)]
    public bool Expedited { get; set; }
}

/// <summary>DATA-14 — a blob inside a small object: carrying cost with traversal taken out.</summary>
internal sealed class BlobEnvelope
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];
}

/// <summary>DATA-15 — bulk numeric arrays, where a layout-fixed serializer legitimately wins.</summary>
internal sealed class NumericArrays
{
    public int[] Integers { get; set; } = [];

    public long[] Longs { get; set; } = [];

    public double[] Doubles { get; set; } = [];
}

/// <summary>DATA-17 — mostly-null members, so null framing is what is measured.</summary>
internal sealed class NullSparse
{
    public int Id { get; set; }

    public string? A { get; set; }

    public string? B { get; set; }

    public string? C { get; set; }

    public string? D { get; set; }

    public string? E { get; set; }

    public int? F { get; set; }

    public int? G { get; set; }

    public long? H { get; set; }

    public double? I { get; set; }

    public decimal? J { get; set; }

    public Guid? K { get; set; }

    public DateTime? L { get; set; }

    public TimeSpan? M { get; set; }

    public Address? N { get; set; }

    public Address? O { get; set; }

    public Measurement? P { get; set; }

    public List<string>? Q { get; set; }

    public List<int>? R { get; set; }

    public Dictionary<string, string>? S { get; set; }

    public int[]? T { get; set; }
}

/// <summary>DATA-19 — one instance of each container family the contract supports.</summary>
internal sealed class CollectionZoo
{
    public int[] Array { get; set; } = [];

    public List<string> List { get; set; } = [];

    public HashSet<int> Set { get; set; } = [];

    public SortedSet<int> SortedSet { get; set; } = [];

    public LinkedList<string> Linked { get; set; } = new();

    public Stack<int> Stack { get; set; } = new();

    public Queue<int> Queue { get; set; } = new();

    public Dictionary<string, int> Dictionary { get; set; } = [];

    public SortedDictionary<string, int> SortedDictionary { get; set; } = [];

    public ImmutableArray<int> ImmutableArray { get; set; } = [];

    public ImmutableList<string> ImmutableList { get; set; } = ImmutableList<string>.Empty;

    public ImmutableDictionary<string, int> ImmutableDictionary { get; set; } =
        System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;

    public KeyValuePair<string, int> Pair { get; set; }

    public (int Number, string Text) Tuple { get; set; }

    public int[,] Rectangular { get; set; } = new int[0, 0];
}

/// <summary>DATA-20 — the families other serializers most often lack natively.</summary>
internal sealed class TimeAndNumerics
{
    public DateTime Utc { get; set; }

    public DateTime Local { get; set; }

    public DateTimeOffset Offset { get; set; }

    public TimeSpan Elapsed { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly Time { get; set; }

    public decimal Money { get; set; }

    public Int128 Wide { get; set; }

    public UInt128 WideUnsigned { get; set; }

    public BigInteger Big { get; set; }

    public Half Small { get; set; }

    public Complex Complex { get; set; }

    public Vector2 Vector2 { get; set; }

    public Vector3 Vector3 { get; set; }

    public Vector4 Vector4 { get; set; }

    public Quaternion Quaternion { get; set; }

    public Matrix3x2 Matrix3X2 { get; set; }

    public Matrix4x4 Matrix4X4 { get; set; }

    public Guid Guid { get; set; }

    public Version Version { get; set; } = new(1, 0);

    public Uri Uri { get; set; } = new("https://example.invalid/");
}
