// The corpus models as MemoryPack consumes them: every type of the schema — the Viper
// models — under the same name with the same members, carrying the attributes this library needs, and
// the probe models of the plan's §7.5. ModelSchema.Failures holds them to the schema (FAIR-19).

using System.Collections.Immutable;
using System.Numerics;
using MemoryPack;

namespace ViShap.Viper.Serialization.Benchmarks.Models.MemoryPackVariant;

public enum Channel
{
    Unknown = 0,
    Web = 1,
    Mobile = 2,
    Partner = 3,
    Internal = 4,
}

public enum Severity : byte
{
    Trace = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4,
}

[MemoryPackable]
public sealed partial class TinyFlat
{
    public int Id { get; set; }

    public long Sequence { get; set; }

    public short Region { get; set; }

    public byte Version { get; set; }

    public bool Active { get; set; }

    public double Score { get; set; }

    public float Ratio { get; set; }

    public decimal Amount { get; set; }

    public Channel Channel { get; set; }

    public string Label { get; set; } = string.Empty;

    public Guid Reference { get; set; }

    public DateTime CreatedUtc { get; set; }
}

[MemoryPackable]
public sealed partial class Address
{
    public string Line1 { get; set; } = string.Empty;

    public string? Line2 { get; set; }

    public string City { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

[MemoryPackable]
public sealed partial class Measurement
{
    public string Metric { get; set; } = string.Empty;

    public double Value { get; set; }

    public Severity Severity { get; set; }

    public DateTime TakenUtc { get; set; }

    public bool Estimated { get; set; }
}

[MemoryPackable]
public sealed partial class MediumObject
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Revision { get; set; }

    public long ExternalId { get; set; }

    public short Priority { get; set; }

    public byte Flags { get; set; }

    public bool Published { get; set; }

    public bool Archived { get; set; }

    public double Weight { get; set; }

    public double Confidence { get; set; }

    public float Scale { get; set; }

    public decimal Price { get; set; }

    public decimal Discount { get; set; }

    public Channel Channel { get; set; }

    public Severity Severity { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public DateTime? DeletedUtc { get; set; }

    public TimeSpan Duration { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? DelegateId { get; set; }

    public int? Quota { get; set; }

    public double? Threshold { get; set; }

    public string Culture { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public Address Primary { get; set; } = new();

    public Address? Secondary { get; set; }

    public List<Measurement> Measurements { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public Dictionary<string, string> Attributes { get; set; } = [];

    public int[] Counters { get; set; } = [];
}

[MemoryPackable]
public sealed partial class ScalarRecord
{
    public int Id { get; set; }

    public long Ticks { get; set; }

    public double Value { get; set; }

    public bool Valid { get; set; }

    public string Code { get; set; } = string.Empty;
}

[MemoryPackable]
public sealed partial class DeepNode
{
    public int Level { get; set; }

    public string Label { get; set; } = string.Empty;

    public long Payload { get; set; }

    public DeepNode? Child { get; set; }
}

[MemoryPackable]
public sealed partial class DagNode
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Weight { get; set; }

    public List<DagNode> Children { get; set; } = [];
}

[MemoryPackable]
public sealed partial class CyclicNode
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public CyclicNode? Parent { get; set; }

    public List<CyclicNode> Children { get; set; } = [];
}

[MemoryPackable]
public sealed partial class BlobEnvelope
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];
}

[MemoryPackable]
public sealed partial class NumericArrays
{
    public int[] Integers { get; set; } = [];

    public long[] Longs { get; set; } = [];

    public double[] Doubles { get; set; } = [];
}

[MemoryPackable]
public sealed partial class NullSparse
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

[MemoryPackable]
public sealed partial class WideObject
{
    public string M001 { get; set; } = string.Empty;

    public double M002 { get; set; }

    public bool M003 { get; set; }

    public long M004 { get; set; }

    public decimal M005 { get; set; }

    public Guid M006 { get; set; }

    public DateTime M007 { get; set; }

    public int M008 { get; set; }

    public string M009 { get; set; } = string.Empty;

    public double M010 { get; set; }

    public bool M011 { get; set; }

    public long M012 { get; set; }

    public decimal M013 { get; set; }

    public Guid M014 { get; set; }

    public DateTime M015 { get; set; }

    public int M016 { get; set; }

    public string M017 { get; set; } = string.Empty;

    public double M018 { get; set; }

    public bool M019 { get; set; }

    public long M020 { get; set; }

    public decimal M021 { get; set; }

    public Guid M022 { get; set; }

    public DateTime M023 { get; set; }

    public int M024 { get; set; }

    public string M025 { get; set; } = string.Empty;

    public double M026 { get; set; }

    public bool M027 { get; set; }

    public long M028 { get; set; }

    public decimal M029 { get; set; }

    public Guid M030 { get; set; }

    public DateTime M031 { get; set; }

    public int M032 { get; set; }

    public string M033 { get; set; } = string.Empty;

    public double M034 { get; set; }

    public bool M035 { get; set; }

    public long M036 { get; set; }

    public decimal M037 { get; set; }

    public Guid M038 { get; set; }

    public DateTime M039 { get; set; }

    public int M040 { get; set; }

    public string M041 { get; set; } = string.Empty;

    public double M042 { get; set; }

    public bool M043 { get; set; }

    public long M044 { get; set; }

    public decimal M045 { get; set; }

    public Guid M046 { get; set; }

    public DateTime M047 { get; set; }

    public int M048 { get; set; }

    public string M049 { get; set; } = string.Empty;

    public double M050 { get; set; }

    public bool M051 { get; set; }

    public long M052 { get; set; }

    public decimal M053 { get; set; }

    public Guid M054 { get; set; }

    public DateTime M055 { get; set; }

    public int M056 { get; set; }

    public string M057 { get; set; } = string.Empty;

    public double M058 { get; set; }

    public bool M059 { get; set; }

    public long M060 { get; set; }

    public decimal M061 { get; set; }

    public Guid M062 { get; set; }

    public DateTime M063 { get; set; }

    public int M064 { get; set; }

    public string M065 { get; set; } = string.Empty;

    public double M066 { get; set; }

    public bool M067 { get; set; }

    public long M068 { get; set; }

    public decimal M069 { get; set; }

    public Guid M070 { get; set; }

    public DateTime M071 { get; set; }

    public int M072 { get; set; }

    public string M073 { get; set; } = string.Empty;

    public double M074 { get; set; }

    public bool M075 { get; set; }

    public long M076 { get; set; }

    public decimal M077 { get; set; }

    public Guid M078 { get; set; }

    public DateTime M079 { get; set; }

    public int M080 { get; set; }

    public string M081 { get; set; } = string.Empty;

    public double M082 { get; set; }

    public bool M083 { get; set; }

    public long M084 { get; set; }

    public decimal M085 { get; set; }

    public Guid M086 { get; set; }

    public DateTime M087 { get; set; }

    public int M088 { get; set; }

    public string M089 { get; set; } = string.Empty;

    public double M090 { get; set; }

    public bool M091 { get; set; }

    public long M092 { get; set; }

    public decimal M093 { get; set; }

    public Guid M094 { get; set; }

    public DateTime M095 { get; set; }

    public int M096 { get; set; }

    public string M097 { get; set; } = string.Empty;

    public double M098 { get; set; }

    public bool M099 { get; set; }

    public long M100 { get; set; }

    public decimal M101 { get; set; }

    public Guid M102 { get; set; }

    public DateTime M103 { get; set; }

    public int M104 { get; set; }

    public string M105 { get; set; } = string.Empty;

    public double M106 { get; set; }

    public bool M107 { get; set; }

    public long M108 { get; set; }

    public decimal M109 { get; set; }

    public Guid M110 { get; set; }

    public DateTime M111 { get; set; }

    public int M112 { get; set; }

    public string M113 { get; set; } = string.Empty;

    public double M114 { get; set; }

    public bool M115 { get; set; }

    public long M116 { get; set; }

    public decimal M117 { get; set; }

    public Guid M118 { get; set; }

    public DateTime M119 { get; set; }

    public int M120 { get; set; }

    public string M121 { get; set; } = string.Empty;

    public double M122 { get; set; }

    public bool M123 { get; set; }

    public long M124 { get; set; }

    public decimal M125 { get; set; }

    public Guid M126 { get; set; }

    public DateTime M127 { get; set; }

    public int M128 { get; set; }

    public string M129 { get; set; } = string.Empty;

    public double M130 { get; set; }

    public bool M131 { get; set; }

    public long M132 { get; set; }

    public decimal M133 { get; set; }

    public Guid M134 { get; set; }

    public DateTime M135 { get; set; }

    public int M136 { get; set; }

    public string M137 { get; set; } = string.Empty;

    public double M138 { get; set; }

    public bool M139 { get; set; }

    public long M140 { get; set; }

    public decimal M141 { get; set; }

    public Guid M142 { get; set; }

    public DateTime M143 { get; set; }

    public int M144 { get; set; }

    public string M145 { get; set; } = string.Empty;

    public double M146 { get; set; }

    public bool M147 { get; set; }

    public long M148 { get; set; }

    public decimal M149 { get; set; }

    public Guid M150 { get; set; }

    public DateTime M151 { get; set; }

    public int M152 { get; set; }

    public string M153 { get; set; } = string.Empty;

    public double M154 { get; set; }

    public bool M155 { get; set; }

    public long M156 { get; set; }

    public decimal M157 { get; set; }

    public Guid M158 { get; set; }

    public DateTime M159 { get; set; }

    public int M160 { get; set; }

    public string M161 { get; set; } = string.Empty;

    public double M162 { get; set; }

    public bool M163 { get; set; }

    public long M164 { get; set; }

    public decimal M165 { get; set; }

    public Guid M166 { get; set; }

    public DateTime M167 { get; set; }

    public int M168 { get; set; }

    public string M169 { get; set; } = string.Empty;

    public double M170 { get; set; }

    public bool M171 { get; set; }

    public long M172 { get; set; }

    public decimal M173 { get; set; }

    public Guid M174 { get; set; }

    public DateTime M175 { get; set; }

    public int M176 { get; set; }

    public string M177 { get; set; } = string.Empty;

    public double M178 { get; set; }

    public bool M179 { get; set; }

    public long M180 { get; set; }

    public decimal M181 { get; set; }

    public Guid M182 { get; set; }

    public DateTime M183 { get; set; }

    public int M184 { get; set; }

    public string M185 { get; set; } = string.Empty;

    public double M186 { get; set; }

    public bool M187 { get; set; }

    public long M188 { get; set; }

    public decimal M189 { get; set; }

    public Guid M190 { get; set; }

    public DateTime M191 { get; set; }

    public int M192 { get; set; }

    public string M193 { get; set; } = string.Empty;

    public double M194 { get; set; }

    public bool M195 { get; set; }

    public long M196 { get; set; }

    public decimal M197 { get; set; }

    public Guid M198 { get; set; }

    public DateTime M199 { get; set; }

    public int M200 { get; set; }
}

[MemoryPackable]
public sealed partial class CollectionZoo
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

    public ImmutableDictionary<int, string> ImmutableDictionary { get; set; } = System.Collections.Immutable.ImmutableDictionary<int, string>.Empty;

    public KeyValuePair<string, int> Pair { get; set; }

    public (int Number, string Text) Tuple { get; set; }

    public int[,] Rectangular { get; set; } = new int[0, 0];
}

[MemoryPackable]
public sealed partial class TimeAndNumerics
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

[MemoryPackable]
public sealed partial class ProbeLeaf
{
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable]
public sealed partial class ProbePair
{
    public ProbeLeaf? Left { get; set; }

    public ProbeLeaf? Right { get; set; }
}

[MemoryPackable]
public sealed partial class ProbeCycle
{
    public string Name { get; set; } = string.Empty;

    public ProbeCycle? Next { get; set; }
}

[MemoryPackable]
public sealed partial class ProbeDeep
{
    public int Level { get; set; }

    public ProbeDeep? Next { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class KeyedOrder
{
    [MemoryPackOrder(0)]
    public Guid Id { get; set; }

    [MemoryPackOrder(1)]
    public string Customer { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public decimal Total { get; set; }

    [MemoryPackOrder(3)]
    public DateTime PlacedUtc { get; set; }

    [MemoryPackOrder(4)]
    public Channel Channel { get; set; }

    [MemoryPackOrder(5)]
    public List<KeyedLine> Lines { get; set; } = [];

    [MemoryPackOrder(6)]
    public string? Note { get; set; }

    [MemoryPackOrder(7)]
    public int Revision { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class KeyedLine
{
    [MemoryPackOrder(0)]
    public string Sku { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public int Quantity { get; set; }

    [MemoryPackOrder(2)]
    public decimal UnitPrice { get; set; }

    [MemoryPackOrder(3)]
    public string? Comment { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class KeyedOrderV2
{
    [MemoryPackOrder(7)]
    public int Revision { get; set; }

    [MemoryPackOrder(0)]
    public Guid Id { get; set; }

    [MemoryPackOrder(2)]
    public decimal Total { get; set; }

    [MemoryPackOrder(5)]
    public List<KeyedLine> Lines { get; set; } = [];

    [MemoryPackOrder(8)]
    public string? Warehouse { get; set; }

    [MemoryPackOrder(9)]
    public bool Expedited { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ProbeRecord
{
    [MemoryPackOrder(0)]
    public int A { get; set; }

    [MemoryPackOrder(1)]
    public string B { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public double C { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ProbeRecordAdded
{
    [MemoryPackOrder(0)]
    public int A { get; set; }

    [MemoryPackOrder(1)]
    public string B { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public double C { get; set; }

    [MemoryPackOrder(3)]
    public long D { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ProbeRecordRemoved
{
    [MemoryPackOrder(0)]
    public int A { get; set; }

    [MemoryPackOrder(2)]
    public double C { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ProbeRecordReordered
{
    [MemoryPackOrder(2)]
    public double C { get; set; }

    [MemoryPackOrder(0)]
    public int A { get; set; }

    [MemoryPackOrder(1)]
    public string B { get; set; } = string.Empty;
}

[MemoryPackable]
[MemoryPackUnion(1, typeof(TextEvent))]
[MemoryPackUnion(2, typeof(NumericEvent))]
[MemoryPackUnion(3, typeof(StateEvent))]
[MemoryPackUnion(4, typeof(LocationEvent))]
[MemoryPackUnion(5, typeof(BatchEvent))]
[MemoryPackUnion(6, typeof(ErrorEvent))]
public abstract partial class EventBase
{
    public long Sequence { get; set; }

    public DateTime AtUtc { get; set; }
}

[MemoryPackable]
public sealed partial class TextEvent : EventBase
{
    public string Text { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}

[MemoryPackable]
public sealed partial class NumericEvent : EventBase
{
    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;
}

[MemoryPackable]
public sealed partial class StateEvent : EventBase
{
    public Severity From { get; set; }

    public Severity To { get; set; }

    public bool Automatic { get; set; }
}

[MemoryPackable]
public sealed partial class LocationEvent : EventBase
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public float Accuracy { get; set; }
}

[MemoryPackable]
public sealed partial class BatchEvent : EventBase
{
    public List<long> Ids { get; set; } = [];

    public int Total { get; set; }
}

[MemoryPackable]
public sealed partial class ErrorEvent : EventBase
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public int Attempt { get; set; }
}

[MemoryPackable]
[MemoryPackUnion(1, typeof(ProbeCircle))]
[MemoryPackUnion(2, typeof(ProbeSquare))]
public abstract partial class ProbeShape
{
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable]
public sealed partial class ProbeCircle : ProbeShape
{
    public double Radius { get; set; }
}

[MemoryPackable]
public sealed partial class ProbeSquare : ProbeShape
{
    public double Side { get; set; }
}
