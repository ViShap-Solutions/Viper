// The corpus models as MessagePack-CSharp consumes them: every type of the schema — the Viper
// models — under the same name with the same members, carrying the attributes this library needs, and
// the probe models of the plan's §7.5. ModelSchema.Failures holds them to the schema (FAIR-19).

using System.Collections.Immutable;
using System.Numerics;
using MessagePack;

namespace ViShap.Viper.Serialization.Benchmarks.Models.MessagePackVariant;

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

[MessagePackObject]
public sealed class TinyFlat
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public long Sequence { get; set; }

    [Key(2)]
    public short Region { get; set; }

    [Key(3)]
    public byte Version { get; set; }

    [Key(4)]
    public bool Active { get; set; }

    [Key(5)]
    public double Score { get; set; }

    [Key(6)]
    public float Ratio { get; set; }

    [Key(7)]
    public decimal Amount { get; set; }

    [Key(8)]
    public Channel Channel { get; set; }

    [Key(9)]
    public string Label { get; set; } = string.Empty;

    [Key(10)]
    public Guid Reference { get; set; }

    [Key(11)]
    public DateTime CreatedUtc { get; set; }
}

[MessagePackObject]
public sealed class Address
{
    [Key(0)]
    public string Line1 { get; set; } = string.Empty;

    [Key(1)]
    public string? Line2 { get; set; }

    [Key(2)]
    public string City { get; set; } = string.Empty;

    [Key(3)]
    public string PostalCode { get; set; } = string.Empty;

    [Key(4)]
    public string CountryCode { get; set; } = string.Empty;

    [Key(5)]
    public double Latitude { get; set; }

    [Key(6)]
    public double Longitude { get; set; }
}

[MessagePackObject]
public sealed class Measurement
{
    [Key(0)]
    public string Metric { get; set; } = string.Empty;

    [Key(1)]
    public double Value { get; set; }

    [Key(2)]
    public Severity Severity { get; set; }

    [Key(3)]
    public DateTime TakenUtc { get; set; }

    [Key(4)]
    public bool Estimated { get; set; }
}

[MessagePackObject]
public sealed class MediumObject
{
    [Key(0)]
    public Guid Id { get; set; }

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public string Description { get; set; } = string.Empty;

    [Key(3)]
    public string? Note { get; set; }

    [Key(4)]
    public int Revision { get; set; }

    [Key(5)]
    public long ExternalId { get; set; }

    [Key(6)]
    public short Priority { get; set; }

    [Key(7)]
    public byte Flags { get; set; }

    [Key(8)]
    public bool Published { get; set; }

    [Key(9)]
    public bool Archived { get; set; }

    [Key(10)]
    public double Weight { get; set; }

    [Key(11)]
    public double Confidence { get; set; }

    [Key(12)]
    public float Scale { get; set; }

    [Key(13)]
    public decimal Price { get; set; }

    [Key(14)]
    public decimal Discount { get; set; }

    [Key(15)]
    public Channel Channel { get; set; }

    [Key(16)]
    public Severity Severity { get; set; }

    [Key(17)]
    public DateTime CreatedUtc { get; set; }

    [Key(18)]
    public DateTime UpdatedUtc { get; set; }

    [Key(19)]
    public DateTime? DeletedUtc { get; set; }

    [Key(20)]
    public TimeSpan Duration { get; set; }

    [Key(21)]
    public Guid OwnerId { get; set; }

    [Key(22)]
    public Guid? DelegateId { get; set; }

    [Key(23)]
    public int? Quota { get; set; }

    [Key(24)]
    public double? Threshold { get; set; }

    [Key(25)]
    public string Culture { get; set; } = string.Empty;

    [Key(26)]
    public string Slug { get; set; } = string.Empty;

    [Key(27)]
    public string Category { get; set; } = string.Empty;

    [Key(28)]
    public Address Primary { get; set; } = new();

    [Key(29)]
    public Address? Secondary { get; set; }

    [Key(30)]
    public List<Measurement> Measurements { get; set; } = [];

    [Key(31)]
    public List<string> Tags { get; set; } = [];

    [Key(32)]
    public Dictionary<string, string> Attributes { get; set; } = [];

    [Key(33)]
    public int[] Counters { get; set; } = [];
}

[MessagePackObject]
public sealed class ScalarRecord
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public long Ticks { get; set; }

    [Key(2)]
    public double Value { get; set; }

    [Key(3)]
    public bool Valid { get; set; }

    [Key(4)]
    public string Code { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class DeepNode
{
    [Key(0)]
    public int Level { get; set; }

    [Key(1)]
    public string Label { get; set; } = string.Empty;

    [Key(2)]
    public long Payload { get; set; }

    [Key(3)]
    public DeepNode? Child { get; set; }
}

[MessagePackObject]
public sealed class DagNode
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public double Weight { get; set; }

    [Key(3)]
    public List<DagNode> Children { get; set; } = [];
}

[MessagePackObject]
public sealed class CyclicNode
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public CyclicNode? Parent { get; set; }

    [Key(3)]
    public List<CyclicNode> Children { get; set; } = [];
}

[MessagePackObject]
public sealed class BlobEnvelope
{
    [Key(0)]
    public Guid Id { get; set; }

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public byte[] Content { get; set; } = [];
}

[MessagePackObject]
public sealed class NumericArrays
{
    [Key(0)]
    public int[] Integers { get; set; } = [];

    [Key(1)]
    public long[] Longs { get; set; } = [];

    [Key(2)]
    public double[] Doubles { get; set; } = [];
}

[MessagePackObject]
public sealed class NullSparse
{
    [Key(0)]
    public int Id { get; set; }

    [Key(1)]
    public string? A { get; set; }

    [Key(2)]
    public string? B { get; set; }

    [Key(3)]
    public string? C { get; set; }

    [Key(4)]
    public string? D { get; set; }

    [Key(5)]
    public string? E { get; set; }

    [Key(6)]
    public int? F { get; set; }

    [Key(7)]
    public int? G { get; set; }

    [Key(8)]
    public long? H { get; set; }

    [Key(9)]
    public double? I { get; set; }

    [Key(10)]
    public decimal? J { get; set; }

    [Key(11)]
    public Guid? K { get; set; }

    [Key(12)]
    public DateTime? L { get; set; }

    [Key(13)]
    public TimeSpan? M { get; set; }

    [Key(14)]
    public Address? N { get; set; }

    [Key(15)]
    public Address? O { get; set; }

    [Key(16)]
    public Measurement? P { get; set; }

    [Key(17)]
    public List<string>? Q { get; set; }

    [Key(18)]
    public List<int>? R { get; set; }

    [Key(19)]
    public Dictionary<string, string>? S { get; set; }

    [Key(20)]
    public int[]? T { get; set; }
}

[MessagePackObject]
public sealed class WideObject
{
    [Key(0)]
    public string M001 { get; set; } = string.Empty;

    [Key(1)]
    public double M002 { get; set; }

    [Key(2)]
    public bool M003 { get; set; }

    [Key(3)]
    public long M004 { get; set; }

    [Key(4)]
    public decimal M005 { get; set; }

    [Key(5)]
    public Guid M006 { get; set; }

    [Key(6)]
    public DateTime M007 { get; set; }

    [Key(7)]
    public int M008 { get; set; }

    [Key(8)]
    public string M009 { get; set; } = string.Empty;

    [Key(9)]
    public double M010 { get; set; }

    [Key(10)]
    public bool M011 { get; set; }

    [Key(11)]
    public long M012 { get; set; }

    [Key(12)]
    public decimal M013 { get; set; }

    [Key(13)]
    public Guid M014 { get; set; }

    [Key(14)]
    public DateTime M015 { get; set; }

    [Key(15)]
    public int M016 { get; set; }

    [Key(16)]
    public string M017 { get; set; } = string.Empty;

    [Key(17)]
    public double M018 { get; set; }

    [Key(18)]
    public bool M019 { get; set; }

    [Key(19)]
    public long M020 { get; set; }

    [Key(20)]
    public decimal M021 { get; set; }

    [Key(21)]
    public Guid M022 { get; set; }

    [Key(22)]
    public DateTime M023 { get; set; }

    [Key(23)]
    public int M024 { get; set; }

    [Key(24)]
    public string M025 { get; set; } = string.Empty;

    [Key(25)]
    public double M026 { get; set; }

    [Key(26)]
    public bool M027 { get; set; }

    [Key(27)]
    public long M028 { get; set; }

    [Key(28)]
    public decimal M029 { get; set; }

    [Key(29)]
    public Guid M030 { get; set; }

    [Key(30)]
    public DateTime M031 { get; set; }

    [Key(31)]
    public int M032 { get; set; }

    [Key(32)]
    public string M033 { get; set; } = string.Empty;

    [Key(33)]
    public double M034 { get; set; }

    [Key(34)]
    public bool M035 { get; set; }

    [Key(35)]
    public long M036 { get; set; }

    [Key(36)]
    public decimal M037 { get; set; }

    [Key(37)]
    public Guid M038 { get; set; }

    [Key(38)]
    public DateTime M039 { get; set; }

    [Key(39)]
    public int M040 { get; set; }

    [Key(40)]
    public string M041 { get; set; } = string.Empty;

    [Key(41)]
    public double M042 { get; set; }

    [Key(42)]
    public bool M043 { get; set; }

    [Key(43)]
    public long M044 { get; set; }

    [Key(44)]
    public decimal M045 { get; set; }

    [Key(45)]
    public Guid M046 { get; set; }

    [Key(46)]
    public DateTime M047 { get; set; }

    [Key(47)]
    public int M048 { get; set; }

    [Key(48)]
    public string M049 { get; set; } = string.Empty;

    [Key(49)]
    public double M050 { get; set; }

    [Key(50)]
    public bool M051 { get; set; }

    [Key(51)]
    public long M052 { get; set; }

    [Key(52)]
    public decimal M053 { get; set; }

    [Key(53)]
    public Guid M054 { get; set; }

    [Key(54)]
    public DateTime M055 { get; set; }

    [Key(55)]
    public int M056 { get; set; }

    [Key(56)]
    public string M057 { get; set; } = string.Empty;

    [Key(57)]
    public double M058 { get; set; }

    [Key(58)]
    public bool M059 { get; set; }

    [Key(59)]
    public long M060 { get; set; }

    [Key(60)]
    public decimal M061 { get; set; }

    [Key(61)]
    public Guid M062 { get; set; }

    [Key(62)]
    public DateTime M063 { get; set; }

    [Key(63)]
    public int M064 { get; set; }

    [Key(64)]
    public string M065 { get; set; } = string.Empty;

    [Key(65)]
    public double M066 { get; set; }

    [Key(66)]
    public bool M067 { get; set; }

    [Key(67)]
    public long M068 { get; set; }

    [Key(68)]
    public decimal M069 { get; set; }

    [Key(69)]
    public Guid M070 { get; set; }

    [Key(70)]
    public DateTime M071 { get; set; }

    [Key(71)]
    public int M072 { get; set; }

    [Key(72)]
    public string M073 { get; set; } = string.Empty;

    [Key(73)]
    public double M074 { get; set; }

    [Key(74)]
    public bool M075 { get; set; }

    [Key(75)]
    public long M076 { get; set; }

    [Key(76)]
    public decimal M077 { get; set; }

    [Key(77)]
    public Guid M078 { get; set; }

    [Key(78)]
    public DateTime M079 { get; set; }

    [Key(79)]
    public int M080 { get; set; }

    [Key(80)]
    public string M081 { get; set; } = string.Empty;

    [Key(81)]
    public double M082 { get; set; }

    [Key(82)]
    public bool M083 { get; set; }

    [Key(83)]
    public long M084 { get; set; }

    [Key(84)]
    public decimal M085 { get; set; }

    [Key(85)]
    public Guid M086 { get; set; }

    [Key(86)]
    public DateTime M087 { get; set; }

    [Key(87)]
    public int M088 { get; set; }

    [Key(88)]
    public string M089 { get; set; } = string.Empty;

    [Key(89)]
    public double M090 { get; set; }

    [Key(90)]
    public bool M091 { get; set; }

    [Key(91)]
    public long M092 { get; set; }

    [Key(92)]
    public decimal M093 { get; set; }

    [Key(93)]
    public Guid M094 { get; set; }

    [Key(94)]
    public DateTime M095 { get; set; }

    [Key(95)]
    public int M096 { get; set; }

    [Key(96)]
    public string M097 { get; set; } = string.Empty;

    [Key(97)]
    public double M098 { get; set; }

    [Key(98)]
    public bool M099 { get; set; }

    [Key(99)]
    public long M100 { get; set; }

    [Key(100)]
    public decimal M101 { get; set; }

    [Key(101)]
    public Guid M102 { get; set; }

    [Key(102)]
    public DateTime M103 { get; set; }

    [Key(103)]
    public int M104 { get; set; }

    [Key(104)]
    public string M105 { get; set; } = string.Empty;

    [Key(105)]
    public double M106 { get; set; }

    [Key(106)]
    public bool M107 { get; set; }

    [Key(107)]
    public long M108 { get; set; }

    [Key(108)]
    public decimal M109 { get; set; }

    [Key(109)]
    public Guid M110 { get; set; }

    [Key(110)]
    public DateTime M111 { get; set; }

    [Key(111)]
    public int M112 { get; set; }

    [Key(112)]
    public string M113 { get; set; } = string.Empty;

    [Key(113)]
    public double M114 { get; set; }

    [Key(114)]
    public bool M115 { get; set; }

    [Key(115)]
    public long M116 { get; set; }

    [Key(116)]
    public decimal M117 { get; set; }

    [Key(117)]
    public Guid M118 { get; set; }

    [Key(118)]
    public DateTime M119 { get; set; }

    [Key(119)]
    public int M120 { get; set; }

    [Key(120)]
    public string M121 { get; set; } = string.Empty;

    [Key(121)]
    public double M122 { get; set; }

    [Key(122)]
    public bool M123 { get; set; }

    [Key(123)]
    public long M124 { get; set; }

    [Key(124)]
    public decimal M125 { get; set; }

    [Key(125)]
    public Guid M126 { get; set; }

    [Key(126)]
    public DateTime M127 { get; set; }

    [Key(127)]
    public int M128 { get; set; }

    [Key(128)]
    public string M129 { get; set; } = string.Empty;

    [Key(129)]
    public double M130 { get; set; }

    [Key(130)]
    public bool M131 { get; set; }

    [Key(131)]
    public long M132 { get; set; }

    [Key(132)]
    public decimal M133 { get; set; }

    [Key(133)]
    public Guid M134 { get; set; }

    [Key(134)]
    public DateTime M135 { get; set; }

    [Key(135)]
    public int M136 { get; set; }

    [Key(136)]
    public string M137 { get; set; } = string.Empty;

    [Key(137)]
    public double M138 { get; set; }

    [Key(138)]
    public bool M139 { get; set; }

    [Key(139)]
    public long M140 { get; set; }

    [Key(140)]
    public decimal M141 { get; set; }

    [Key(141)]
    public Guid M142 { get; set; }

    [Key(142)]
    public DateTime M143 { get; set; }

    [Key(143)]
    public int M144 { get; set; }

    [Key(144)]
    public string M145 { get; set; } = string.Empty;

    [Key(145)]
    public double M146 { get; set; }

    [Key(146)]
    public bool M147 { get; set; }

    [Key(147)]
    public long M148 { get; set; }

    [Key(148)]
    public decimal M149 { get; set; }

    [Key(149)]
    public Guid M150 { get; set; }

    [Key(150)]
    public DateTime M151 { get; set; }

    [Key(151)]
    public int M152 { get; set; }

    [Key(152)]
    public string M153 { get; set; } = string.Empty;

    [Key(153)]
    public double M154 { get; set; }

    [Key(154)]
    public bool M155 { get; set; }

    [Key(155)]
    public long M156 { get; set; }

    [Key(156)]
    public decimal M157 { get; set; }

    [Key(157)]
    public Guid M158 { get; set; }

    [Key(158)]
    public DateTime M159 { get; set; }

    [Key(159)]
    public int M160 { get; set; }

    [Key(160)]
    public string M161 { get; set; } = string.Empty;

    [Key(161)]
    public double M162 { get; set; }

    [Key(162)]
    public bool M163 { get; set; }

    [Key(163)]
    public long M164 { get; set; }

    [Key(164)]
    public decimal M165 { get; set; }

    [Key(165)]
    public Guid M166 { get; set; }

    [Key(166)]
    public DateTime M167 { get; set; }

    [Key(167)]
    public int M168 { get; set; }

    [Key(168)]
    public string M169 { get; set; } = string.Empty;

    [Key(169)]
    public double M170 { get; set; }

    [Key(170)]
    public bool M171 { get; set; }

    [Key(171)]
    public long M172 { get; set; }

    [Key(172)]
    public decimal M173 { get; set; }

    [Key(173)]
    public Guid M174 { get; set; }

    [Key(174)]
    public DateTime M175 { get; set; }

    [Key(175)]
    public int M176 { get; set; }

    [Key(176)]
    public string M177 { get; set; } = string.Empty;

    [Key(177)]
    public double M178 { get; set; }

    [Key(178)]
    public bool M179 { get; set; }

    [Key(179)]
    public long M180 { get; set; }

    [Key(180)]
    public decimal M181 { get; set; }

    [Key(181)]
    public Guid M182 { get; set; }

    [Key(182)]
    public DateTime M183 { get; set; }

    [Key(183)]
    public int M184 { get; set; }

    [Key(184)]
    public string M185 { get; set; } = string.Empty;

    [Key(185)]
    public double M186 { get; set; }

    [Key(186)]
    public bool M187 { get; set; }

    [Key(187)]
    public long M188 { get; set; }

    [Key(188)]
    public decimal M189 { get; set; }

    [Key(189)]
    public Guid M190 { get; set; }

    [Key(190)]
    public DateTime M191 { get; set; }

    [Key(191)]
    public int M192 { get; set; }

    [Key(192)]
    public string M193 { get; set; } = string.Empty;

    [Key(193)]
    public double M194 { get; set; }

    [Key(194)]
    public bool M195 { get; set; }

    [Key(195)]
    public long M196 { get; set; }

    [Key(196)]
    public decimal M197 { get; set; }

    [Key(197)]
    public Guid M198 { get; set; }

    [Key(198)]
    public DateTime M199 { get; set; }

    [Key(199)]
    public int M200 { get; set; }
}

[MessagePackObject]
public sealed class CollectionZoo
{
    [Key(0)]
    public int[] Array { get; set; } = [];

    [Key(1)]
    public List<string> List { get; set; } = [];

    [Key(2)]
    public HashSet<int> Set { get; set; } = [];

    [Key(3)]
    public SortedSet<int> SortedSet { get; set; } = [];

    [Key(4)]
    public LinkedList<string> Linked { get; set; } = new();

    [Key(5)]
    public Stack<int> Stack { get; set; } = new();

    [Key(6)]
    public Queue<int> Queue { get; set; } = new();

    [Key(7)]
    public Dictionary<string, int> Dictionary { get; set; } = [];

    [Key(8)]
    public SortedDictionary<string, int> SortedDictionary { get; set; } = [];

    [Key(9)]
    public ImmutableArray<int> ImmutableArray { get; set; } = [];

    [Key(10)]
    public ImmutableList<string> ImmutableList { get; set; } = ImmutableList<string>.Empty;

    [Key(11)]
    public ImmutableDictionary<int, string> ImmutableDictionary { get; set; } = System.Collections.Immutable.ImmutableDictionary<int, string>.Empty;

    [Key(12)]
    public KeyValuePair<string, int> Pair { get; set; }

    [Key(13)]
    public (int Number, string Text) Tuple { get; set; }

    [Key(14)]
    public int[,] Rectangular { get; set; } = new int[0, 0];
}

[MessagePackObject]
public sealed class TimeAndNumerics
{
    [Key(0)]
    public DateTime Utc { get; set; }

    [Key(1)]
    public DateTime Local { get; set; }

    [Key(2)]
    public DateTimeOffset Offset { get; set; }

    [Key(3)]
    public TimeSpan Elapsed { get; set; }

    [Key(4)]
    public DateOnly Date { get; set; }

    [Key(5)]
    public TimeOnly Time { get; set; }

    [Key(6)]
    public decimal Money { get; set; }

    [Key(7)]
    public Int128 Wide { get; set; }

    [Key(8)]
    public UInt128 WideUnsigned { get; set; }

    [Key(9)]
    public BigInteger Big { get; set; }

    [Key(10)]
    public Half Small { get; set; }

    [Key(11)]
    public Complex Complex { get; set; }

    [Key(12)]
    public Vector2 Vector2 { get; set; }

    [Key(13)]
    public Vector3 Vector3 { get; set; }

    [Key(14)]
    public Vector4 Vector4 { get; set; }

    [Key(15)]
    public Quaternion Quaternion { get; set; }

    [Key(16)]
    public Matrix3x2 Matrix3X2 { get; set; }

    [Key(17)]
    public Matrix4x4 Matrix4X4 { get; set; }

    [Key(18)]
    public Guid Guid { get; set; }

    [Key(19)]
    public Version Version { get; set; } = new(1, 0);

    [Key(20)]
    public Uri Uri { get; set; } = new("https://example.invalid/");
}

[MessagePackObject]
public sealed class ProbeLeaf
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class ProbePair
{
    [Key(0)]
    public ProbeLeaf? Left { get; set; }

    [Key(1)]
    public ProbeLeaf? Right { get; set; }
}

[MessagePackObject]
public sealed class ProbeCycle
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public ProbeCycle? Next { get; set; }
}

[MessagePackObject]
public sealed class ProbeDeep
{
    [Key(0)]
    public int Level { get; set; }

    [Key(1)]
    public ProbeDeep? Next { get; set; }
}

[MessagePackObject]
public sealed class KeyedOrder
{
    [Key(0)]
    public Guid Id { get; set; }

    [Key(1)]
    public string Customer { get; set; } = string.Empty;

    [Key(2)]
    public decimal Total { get; set; }

    [Key(3)]
    public DateTime PlacedUtc { get; set; }

    [Key(4)]
    public Channel Channel { get; set; }

    [Key(5)]
    public List<KeyedLine> Lines { get; set; } = [];

    [Key(6)]
    public string? Note { get; set; }

    [Key(7)]
    public int Revision { get; set; }
}

[MessagePackObject]
public sealed class KeyedLine
{
    [Key(0)]
    public string Sku { get; set; } = string.Empty;

    [Key(1)]
    public int Quantity { get; set; }

    [Key(2)]
    public decimal UnitPrice { get; set; }

    [Key(3)]
    public string? Comment { get; set; }
}

[MessagePackObject]
public sealed class KeyedOrderV2
{
    [Key(7)]
    public int Revision { get; set; }

    [Key(0)]
    public Guid Id { get; set; }

    [Key(2)]
    public decimal Total { get; set; }

    [Key(5)]
    public List<KeyedLine> Lines { get; set; } = [];

    [Key(8)]
    public string? Warehouse { get; set; }

    [Key(9)]
    public bool Expedited { get; set; }
}

[MessagePackObject]
public sealed class ProbeRecord
{
    [Key(0)]
    public int A { get; set; }

    [Key(1)]
    public string B { get; set; } = string.Empty;

    [Key(2)]
    public double C { get; set; }
}

[MessagePackObject]
public sealed class ProbeRecordAdded
{
    [Key(0)]
    public int A { get; set; }

    [Key(1)]
    public string B { get; set; } = string.Empty;

    [Key(2)]
    public double C { get; set; }

    [Key(3)]
    public long D { get; set; }
}

[MessagePackObject]
public sealed class ProbeRecordRemoved
{
    [Key(0)]
    public int A { get; set; }

    [Key(2)]
    public double C { get; set; }
}

[MessagePackObject]
public sealed class ProbeRecordReordered
{
    [Key(2)]
    public double C { get; set; }

    [Key(0)]
    public int A { get; set; }

    [Key(1)]
    public string B { get; set; } = string.Empty;
}

[Union(1, typeof(TextEvent))]
[Union(2, typeof(NumericEvent))]
[Union(3, typeof(StateEvent))]
[Union(4, typeof(LocationEvent))]
[Union(5, typeof(BatchEvent))]
[Union(6, typeof(ErrorEvent))]
[MessagePackObject]
public abstract class EventBase
{
    [Key(0)]
    public long Sequence { get; set; }

    [Key(1)]
    public DateTime AtUtc { get; set; }
}

[MessagePackObject]
public sealed class TextEvent : EventBase
{
    [Key(2)]
    public string Text { get; set; } = string.Empty;

    [Key(3)]
    public string Source { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class NumericEvent : EventBase
{
    [Key(2)]
    public double Value { get; set; }

    [Key(3)]
    public string Unit { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class StateEvent : EventBase
{
    [Key(2)]
    public Severity From { get; set; }

    [Key(3)]
    public Severity To { get; set; }

    [Key(4)]
    public bool Automatic { get; set; }
}

[MessagePackObject]
public sealed class LocationEvent : EventBase
{
    [Key(2)]
    public double Latitude { get; set; }

    [Key(3)]
    public double Longitude { get; set; }

    [Key(4)]
    public float Accuracy { get; set; }
}

[MessagePackObject]
public sealed class BatchEvent : EventBase
{
    [Key(2)]
    public List<long> Ids { get; set; } = [];

    [Key(3)]
    public int Total { get; set; }
}

[MessagePackObject]
public sealed class ErrorEvent : EventBase
{
    [Key(2)]
    public string Code { get; set; } = string.Empty;

    [Key(3)]
    public string Message { get; set; } = string.Empty;

    [Key(4)]
    public int Attempt { get; set; }
}

[Union(1, typeof(ProbeCircle))]
[Union(2, typeof(ProbeSquare))]
[MessagePackObject]
public abstract class ProbeShape
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class ProbeCircle : ProbeShape
{
    [Key(1)]
    public double Radius { get; set; }
}

[MessagePackObject]
public sealed class ProbeSquare : ProbeShape
{
    [Key(1)]
    public double Side { get; set; }
}
