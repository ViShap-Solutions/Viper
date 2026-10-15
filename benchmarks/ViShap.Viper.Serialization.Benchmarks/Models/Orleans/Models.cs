// The corpus models as Microsoft.Orleans.Serialization consumes them: every type of the schema — the Viper
// models — under the same name with the same members, carrying the attributes this library needs, and
// the probe models of the plan's §7.5. ModelSchema.Failures holds them to the schema (FAIR-19).

using System.Collections.Immutable;
using System.Numerics;
using Orleans;

namespace ViShap.Viper.Serialization.Benchmarks.Models.OrleansVariant;

[GenerateSerializer]
public enum Channel
{
    Unknown = 0,
    Web = 1,
    Mobile = 2,
    Partner = 3,
    Internal = 4,
}

[GenerateSerializer]
public enum Severity : byte
{
    Trace = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4,
}

[GenerateSerializer]
public sealed class TinyFlat
{
    [Id(0)]
    public int Id { get; set; }

    [Id(1)]
    public long Sequence { get; set; }

    [Id(2)]
    public short Region { get; set; }

    [Id(3)]
    public byte Version { get; set; }

    [Id(4)]
    public bool Active { get; set; }

    [Id(5)]
    public double Score { get; set; }

    [Id(6)]
    public float Ratio { get; set; }

    [Id(7)]
    public decimal Amount { get; set; }

    [Id(8)]
    public Channel Channel { get; set; }

    [Id(9)]
    public string Label { get; set; } = string.Empty;

    [Id(10)]
    public Guid Reference { get; set; }

    [Id(11)]
    public DateTime CreatedUtc { get; set; }
}

[GenerateSerializer]
public sealed class Address
{
    [Id(0)]
    public string Line1 { get; set; } = string.Empty;

    [Id(1)]
    public string? Line2 { get; set; }

    [Id(2)]
    public string City { get; set; } = string.Empty;

    [Id(3)]
    public string PostalCode { get; set; } = string.Empty;

    [Id(4)]
    public string CountryCode { get; set; } = string.Empty;

    [Id(5)]
    public double Latitude { get; set; }

    [Id(6)]
    public double Longitude { get; set; }
}

[GenerateSerializer]
public sealed class Measurement
{
    [Id(0)]
    public string Metric { get; set; } = string.Empty;

    [Id(1)]
    public double Value { get; set; }

    [Id(2)]
    public Severity Severity { get; set; }

    [Id(3)]
    public DateTime TakenUtc { get; set; }

    [Id(4)]
    public bool Estimated { get; set; }
}

[GenerateSerializer]
public sealed class MediumObject
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public string Description { get; set; } = string.Empty;

    [Id(3)]
    public string? Note { get; set; }

    [Id(4)]
    public int Revision { get; set; }

    [Id(5)]
    public long ExternalId { get; set; }

    [Id(6)]
    public short Priority { get; set; }

    [Id(7)]
    public byte Flags { get; set; }

    [Id(8)]
    public bool Published { get; set; }

    [Id(9)]
    public bool Archived { get; set; }

    [Id(10)]
    public double Weight { get; set; }

    [Id(11)]
    public double Confidence { get; set; }

    [Id(12)]
    public float Scale { get; set; }

    [Id(13)]
    public decimal Price { get; set; }

    [Id(14)]
    public decimal Discount { get; set; }

    [Id(15)]
    public Channel Channel { get; set; }

    [Id(16)]
    public Severity Severity { get; set; }

    [Id(17)]
    public DateTime CreatedUtc { get; set; }

    [Id(18)]
    public DateTime UpdatedUtc { get; set; }

    [Id(19)]
    public DateTime? DeletedUtc { get; set; }

    [Id(20)]
    public TimeSpan Duration { get; set; }

    [Id(21)]
    public Guid OwnerId { get; set; }

    [Id(22)]
    public Guid? DelegateId { get; set; }

    [Id(23)]
    public int? Quota { get; set; }

    [Id(24)]
    public double? Threshold { get; set; }

    [Id(25)]
    public string Culture { get; set; } = string.Empty;

    [Id(26)]
    public string Slug { get; set; } = string.Empty;

    [Id(27)]
    public string Category { get; set; } = string.Empty;

    [Id(28)]
    public Address Primary { get; set; } = new();

    [Id(29)]
    public Address? Secondary { get; set; }

    [Id(30)]
    public List<Measurement> Measurements { get; set; } = [];

    [Id(31)]
    public List<string> Tags { get; set; } = [];

    [Id(32)]
    public Dictionary<string, string> Attributes { get; set; } = [];

    [Id(33)]
    public int[] Counters { get; set; } = [];
}

[GenerateSerializer]
public sealed class ScalarRecord
{
    [Id(0)]
    public int Id { get; set; }

    [Id(1)]
    public long Ticks { get; set; }

    [Id(2)]
    public double Value { get; set; }

    [Id(3)]
    public bool Valid { get; set; }

    [Id(4)]
    public string Code { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class DeepNode
{
    [Id(0)]
    public int Level { get; set; }

    [Id(1)]
    public string Label { get; set; } = string.Empty;

    [Id(2)]
    public long Payload { get; set; }

    [Id(3)]
    public DeepNode? Child { get; set; }
}

[GenerateSerializer]
public sealed class DagNode
{
    [Id(0)]
    public int Id { get; set; }

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public double Weight { get; set; }

    [Id(3)]
    public List<DagNode> Children { get; set; } = [];
}

[GenerateSerializer]
public sealed class CyclicNode
{
    [Id(0)]
    public int Id { get; set; }

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public CyclicNode? Parent { get; set; }

    [Id(3)]
    public List<CyclicNode> Children { get; set; } = [];
}

[GenerateSerializer]
public sealed class BlobEnvelope
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public byte[] Content { get; set; } = [];
}

[GenerateSerializer]
public sealed class NumericArrays
{
    [Id(0)]
    public int[] Integers { get; set; } = [];

    [Id(1)]
    public long[] Longs { get; set; } = [];

    [Id(2)]
    public double[] Doubles { get; set; } = [];
}

[GenerateSerializer]
public sealed class NullSparse
{
    [Id(0)]
    public int Id { get; set; }

    [Id(1)]
    public string? A { get; set; }

    [Id(2)]
    public string? B { get; set; }

    [Id(3)]
    public string? C { get; set; }

    [Id(4)]
    public string? D { get; set; }

    [Id(5)]
    public string? E { get; set; }

    [Id(6)]
    public int? F { get; set; }

    [Id(7)]
    public int? G { get; set; }

    [Id(8)]
    public long? H { get; set; }

    [Id(9)]
    public double? I { get; set; }

    [Id(10)]
    public decimal? J { get; set; }

    [Id(11)]
    public Guid? K { get; set; }

    [Id(12)]
    public DateTime? L { get; set; }

    [Id(13)]
    public TimeSpan? M { get; set; }

    [Id(14)]
    public Address? N { get; set; }

    [Id(15)]
    public Address? O { get; set; }

    [Id(16)]
    public Measurement? P { get; set; }

    [Id(17)]
    public List<string>? Q { get; set; }

    [Id(18)]
    public List<int>? R { get; set; }

    [Id(19)]
    public Dictionary<string, string>? S { get; set; }

    [Id(20)]
    public int[]? T { get; set; }
}

[GenerateSerializer]
public sealed class WideObject
{
    [Id(0)]
    public string M001 { get; set; } = string.Empty;

    [Id(1)]
    public double M002 { get; set; }

    [Id(2)]
    public bool M003 { get; set; }

    [Id(3)]
    public long M004 { get; set; }

    [Id(4)]
    public decimal M005 { get; set; }

    [Id(5)]
    public Guid M006 { get; set; }

    [Id(6)]
    public DateTime M007 { get; set; }

    [Id(7)]
    public int M008 { get; set; }

    [Id(8)]
    public string M009 { get; set; } = string.Empty;

    [Id(9)]
    public double M010 { get; set; }

    [Id(10)]
    public bool M011 { get; set; }

    [Id(11)]
    public long M012 { get; set; }

    [Id(12)]
    public decimal M013 { get; set; }

    [Id(13)]
    public Guid M014 { get; set; }

    [Id(14)]
    public DateTime M015 { get; set; }

    [Id(15)]
    public int M016 { get; set; }

    [Id(16)]
    public string M017 { get; set; } = string.Empty;

    [Id(17)]
    public double M018 { get; set; }

    [Id(18)]
    public bool M019 { get; set; }

    [Id(19)]
    public long M020 { get; set; }

    [Id(20)]
    public decimal M021 { get; set; }

    [Id(21)]
    public Guid M022 { get; set; }

    [Id(22)]
    public DateTime M023 { get; set; }

    [Id(23)]
    public int M024 { get; set; }

    [Id(24)]
    public string M025 { get; set; } = string.Empty;

    [Id(25)]
    public double M026 { get; set; }

    [Id(26)]
    public bool M027 { get; set; }

    [Id(27)]
    public long M028 { get; set; }

    [Id(28)]
    public decimal M029 { get; set; }

    [Id(29)]
    public Guid M030 { get; set; }

    [Id(30)]
    public DateTime M031 { get; set; }

    [Id(31)]
    public int M032 { get; set; }

    [Id(32)]
    public string M033 { get; set; } = string.Empty;

    [Id(33)]
    public double M034 { get; set; }

    [Id(34)]
    public bool M035 { get; set; }

    [Id(35)]
    public long M036 { get; set; }

    [Id(36)]
    public decimal M037 { get; set; }

    [Id(37)]
    public Guid M038 { get; set; }

    [Id(38)]
    public DateTime M039 { get; set; }

    [Id(39)]
    public int M040 { get; set; }

    [Id(40)]
    public string M041 { get; set; } = string.Empty;

    [Id(41)]
    public double M042 { get; set; }

    [Id(42)]
    public bool M043 { get; set; }

    [Id(43)]
    public long M044 { get; set; }

    [Id(44)]
    public decimal M045 { get; set; }

    [Id(45)]
    public Guid M046 { get; set; }

    [Id(46)]
    public DateTime M047 { get; set; }

    [Id(47)]
    public int M048 { get; set; }

    [Id(48)]
    public string M049 { get; set; } = string.Empty;

    [Id(49)]
    public double M050 { get; set; }

    [Id(50)]
    public bool M051 { get; set; }

    [Id(51)]
    public long M052 { get; set; }

    [Id(52)]
    public decimal M053 { get; set; }

    [Id(53)]
    public Guid M054 { get; set; }

    [Id(54)]
    public DateTime M055 { get; set; }

    [Id(55)]
    public int M056 { get; set; }

    [Id(56)]
    public string M057 { get; set; } = string.Empty;

    [Id(57)]
    public double M058 { get; set; }

    [Id(58)]
    public bool M059 { get; set; }

    [Id(59)]
    public long M060 { get; set; }

    [Id(60)]
    public decimal M061 { get; set; }

    [Id(61)]
    public Guid M062 { get; set; }

    [Id(62)]
    public DateTime M063 { get; set; }

    [Id(63)]
    public int M064 { get; set; }

    [Id(64)]
    public string M065 { get; set; } = string.Empty;

    [Id(65)]
    public double M066 { get; set; }

    [Id(66)]
    public bool M067 { get; set; }

    [Id(67)]
    public long M068 { get; set; }

    [Id(68)]
    public decimal M069 { get; set; }

    [Id(69)]
    public Guid M070 { get; set; }

    [Id(70)]
    public DateTime M071 { get; set; }

    [Id(71)]
    public int M072 { get; set; }

    [Id(72)]
    public string M073 { get; set; } = string.Empty;

    [Id(73)]
    public double M074 { get; set; }

    [Id(74)]
    public bool M075 { get; set; }

    [Id(75)]
    public long M076 { get; set; }

    [Id(76)]
    public decimal M077 { get; set; }

    [Id(77)]
    public Guid M078 { get; set; }

    [Id(78)]
    public DateTime M079 { get; set; }

    [Id(79)]
    public int M080 { get; set; }

    [Id(80)]
    public string M081 { get; set; } = string.Empty;

    [Id(81)]
    public double M082 { get; set; }

    [Id(82)]
    public bool M083 { get; set; }

    [Id(83)]
    public long M084 { get; set; }

    [Id(84)]
    public decimal M085 { get; set; }

    [Id(85)]
    public Guid M086 { get; set; }

    [Id(86)]
    public DateTime M087 { get; set; }

    [Id(87)]
    public int M088 { get; set; }

    [Id(88)]
    public string M089 { get; set; } = string.Empty;

    [Id(89)]
    public double M090 { get; set; }

    [Id(90)]
    public bool M091 { get; set; }

    [Id(91)]
    public long M092 { get; set; }

    [Id(92)]
    public decimal M093 { get; set; }

    [Id(93)]
    public Guid M094 { get; set; }

    [Id(94)]
    public DateTime M095 { get; set; }

    [Id(95)]
    public int M096 { get; set; }

    [Id(96)]
    public string M097 { get; set; } = string.Empty;

    [Id(97)]
    public double M098 { get; set; }

    [Id(98)]
    public bool M099 { get; set; }

    [Id(99)]
    public long M100 { get; set; }

    [Id(100)]
    public decimal M101 { get; set; }

    [Id(101)]
    public Guid M102 { get; set; }

    [Id(102)]
    public DateTime M103 { get; set; }

    [Id(103)]
    public int M104 { get; set; }

    [Id(104)]
    public string M105 { get; set; } = string.Empty;

    [Id(105)]
    public double M106 { get; set; }

    [Id(106)]
    public bool M107 { get; set; }

    [Id(107)]
    public long M108 { get; set; }

    [Id(108)]
    public decimal M109 { get; set; }

    [Id(109)]
    public Guid M110 { get; set; }

    [Id(110)]
    public DateTime M111 { get; set; }

    [Id(111)]
    public int M112 { get; set; }

    [Id(112)]
    public string M113 { get; set; } = string.Empty;

    [Id(113)]
    public double M114 { get; set; }

    [Id(114)]
    public bool M115 { get; set; }

    [Id(115)]
    public long M116 { get; set; }

    [Id(116)]
    public decimal M117 { get; set; }

    [Id(117)]
    public Guid M118 { get; set; }

    [Id(118)]
    public DateTime M119 { get; set; }

    [Id(119)]
    public int M120 { get; set; }

    [Id(120)]
    public string M121 { get; set; } = string.Empty;

    [Id(121)]
    public double M122 { get; set; }

    [Id(122)]
    public bool M123 { get; set; }

    [Id(123)]
    public long M124 { get; set; }

    [Id(124)]
    public decimal M125 { get; set; }

    [Id(125)]
    public Guid M126 { get; set; }

    [Id(126)]
    public DateTime M127 { get; set; }

    [Id(127)]
    public int M128 { get; set; }

    [Id(128)]
    public string M129 { get; set; } = string.Empty;

    [Id(129)]
    public double M130 { get; set; }

    [Id(130)]
    public bool M131 { get; set; }

    [Id(131)]
    public long M132 { get; set; }

    [Id(132)]
    public decimal M133 { get; set; }

    [Id(133)]
    public Guid M134 { get; set; }

    [Id(134)]
    public DateTime M135 { get; set; }

    [Id(135)]
    public int M136 { get; set; }

    [Id(136)]
    public string M137 { get; set; } = string.Empty;

    [Id(137)]
    public double M138 { get; set; }

    [Id(138)]
    public bool M139 { get; set; }

    [Id(139)]
    public long M140 { get; set; }

    [Id(140)]
    public decimal M141 { get; set; }

    [Id(141)]
    public Guid M142 { get; set; }

    [Id(142)]
    public DateTime M143 { get; set; }

    [Id(143)]
    public int M144 { get; set; }

    [Id(144)]
    public string M145 { get; set; } = string.Empty;

    [Id(145)]
    public double M146 { get; set; }

    [Id(146)]
    public bool M147 { get; set; }

    [Id(147)]
    public long M148 { get; set; }

    [Id(148)]
    public decimal M149 { get; set; }

    [Id(149)]
    public Guid M150 { get; set; }

    [Id(150)]
    public DateTime M151 { get; set; }

    [Id(151)]
    public int M152 { get; set; }

    [Id(152)]
    public string M153 { get; set; } = string.Empty;

    [Id(153)]
    public double M154 { get; set; }

    [Id(154)]
    public bool M155 { get; set; }

    [Id(155)]
    public long M156 { get; set; }

    [Id(156)]
    public decimal M157 { get; set; }

    [Id(157)]
    public Guid M158 { get; set; }

    [Id(158)]
    public DateTime M159 { get; set; }

    [Id(159)]
    public int M160 { get; set; }

    [Id(160)]
    public string M161 { get; set; } = string.Empty;

    [Id(161)]
    public double M162 { get; set; }

    [Id(162)]
    public bool M163 { get; set; }

    [Id(163)]
    public long M164 { get; set; }

    [Id(164)]
    public decimal M165 { get; set; }

    [Id(165)]
    public Guid M166 { get; set; }

    [Id(166)]
    public DateTime M167 { get; set; }

    [Id(167)]
    public int M168 { get; set; }

    [Id(168)]
    public string M169 { get; set; } = string.Empty;

    [Id(169)]
    public double M170 { get; set; }

    [Id(170)]
    public bool M171 { get; set; }

    [Id(171)]
    public long M172 { get; set; }

    [Id(172)]
    public decimal M173 { get; set; }

    [Id(173)]
    public Guid M174 { get; set; }

    [Id(174)]
    public DateTime M175 { get; set; }

    [Id(175)]
    public int M176 { get; set; }

    [Id(176)]
    public string M177 { get; set; } = string.Empty;

    [Id(177)]
    public double M178 { get; set; }

    [Id(178)]
    public bool M179 { get; set; }

    [Id(179)]
    public long M180 { get; set; }

    [Id(180)]
    public decimal M181 { get; set; }

    [Id(181)]
    public Guid M182 { get; set; }

    [Id(182)]
    public DateTime M183 { get; set; }

    [Id(183)]
    public int M184 { get; set; }

    [Id(184)]
    public string M185 { get; set; } = string.Empty;

    [Id(185)]
    public double M186 { get; set; }

    [Id(186)]
    public bool M187 { get; set; }

    [Id(187)]
    public long M188 { get; set; }

    [Id(188)]
    public decimal M189 { get; set; }

    [Id(189)]
    public Guid M190 { get; set; }

    [Id(190)]
    public DateTime M191 { get; set; }

    [Id(191)]
    public int M192 { get; set; }

    [Id(192)]
    public string M193 { get; set; } = string.Empty;

    [Id(193)]
    public double M194 { get; set; }

    [Id(194)]
    public bool M195 { get; set; }

    [Id(195)]
    public long M196 { get; set; }

    [Id(196)]
    public decimal M197 { get; set; }

    [Id(197)]
    public Guid M198 { get; set; }

    [Id(198)]
    public DateTime M199 { get; set; }

    [Id(199)]
    public int M200 { get; set; }
}

[GenerateSerializer]
public sealed class CollectionZoo
{
    [Id(0)]
    public int[] Array { get; set; } = [];

    [Id(1)]
    public List<string> List { get; set; } = [];

    [Id(2)]
    public HashSet<int> Set { get; set; } = [];

    [Id(3)]
    public SortedSet<int> SortedSet { get; set; } = [];

    [Id(4)]
    public LinkedList<string> Linked { get; set; } = new();

    [Id(5)]
    public Stack<int> Stack { get; set; } = new();

    [Id(6)]
    public Queue<int> Queue { get; set; } = new();

    [Id(7)]
    public Dictionary<string, int> Dictionary { get; set; } = [];

    [Id(8)]
    public SortedDictionary<string, int> SortedDictionary { get; set; } = [];

    [Id(9)]
    public ImmutableArray<int> ImmutableArray { get; set; } = [];

    [Id(10)]
    public ImmutableList<string> ImmutableList { get; set; } = ImmutableList<string>.Empty;

    [Id(11)]
    public ImmutableDictionary<int, string> ImmutableDictionary { get; set; } = System.Collections.Immutable.ImmutableDictionary<int, string>.Empty;

    [Id(12)]
    public KeyValuePair<string, int> Pair { get; set; }

    [Id(13)]
    public (int Number, string Text) Tuple { get; set; }

    [Id(14)]
    public int[,] Rectangular { get; set; } = new int[0, 0];
}

[GenerateSerializer]
public sealed class TimeAndNumerics
{
    [Id(0)]
    public DateTime Utc { get; set; }

    [Id(1)]
    public DateTime Local { get; set; }

    [Id(2)]
    public DateTimeOffset Offset { get; set; }

    [Id(3)]
    public TimeSpan Elapsed { get; set; }

    [Id(4)]
    public DateOnly Date { get; set; }

    [Id(5)]
    public TimeOnly Time { get; set; }

    [Id(6)]
    public decimal Money { get; set; }

    [Id(7)]
    public Int128 Wide { get; set; }

    [Id(8)]
    public UInt128 WideUnsigned { get; set; }

    [Id(9)]
    public BigInteger Big { get; set; }

    [Id(10)]
    public Half Small { get; set; }

    [Id(11)]
    public Complex Complex { get; set; }

    [Id(12)]
    public Vector2 Vector2 { get; set; }

    [Id(13)]
    public Vector3 Vector3 { get; set; }

    [Id(14)]
    public Vector4 Vector4 { get; set; }

    [Id(15)]
    public Quaternion Quaternion { get; set; }

    [Id(16)]
    public Matrix3x2 Matrix3X2 { get; set; }

    [Id(17)]
    public Matrix4x4 Matrix4X4 { get; set; }

    [Id(18)]
    public Guid Guid { get; set; }

    [Id(19)]
    public Version Version { get; set; } = new(1, 0);

    [Id(20)]
    public Uri Uri { get; set; } = new("https://example.invalid/");
}

[GenerateSerializer]
public sealed class ProbeLeaf
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class ProbePair
{
    [Id(0)]
    public ProbeLeaf? Left { get; set; }

    [Id(1)]
    public ProbeLeaf? Right { get; set; }
}

[GenerateSerializer]
public sealed class ProbeCycle
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;

    [Id(1)]
    public ProbeCycle? Next { get; set; }
}

[GenerateSerializer]
public sealed class ProbeDeep
{
    [Id(0)]
    public int Level { get; set; }

    [Id(1)]
    public ProbeDeep? Next { get; set; }
}

[GenerateSerializer]
public sealed class KeyedOrder
{
    [Id(0)]
    public Guid Id { get; set; }

    [Id(1)]
    public string Customer { get; set; } = string.Empty;

    [Id(2)]
    public decimal Total { get; set; }

    [Id(3)]
    public DateTime PlacedUtc { get; set; }

    [Id(4)]
    public Channel Channel { get; set; }

    [Id(5)]
    public List<KeyedLine> Lines { get; set; } = [];

    [Id(6)]
    public string? Note { get; set; }

    [Id(7)]
    public int Revision { get; set; }
}

[GenerateSerializer]
public sealed class KeyedLine
{
    [Id(0)]
    public string Sku { get; set; } = string.Empty;

    [Id(1)]
    public int Quantity { get; set; }

    [Id(2)]
    public decimal UnitPrice { get; set; }

    [Id(3)]
    public string? Comment { get; set; }
}

[GenerateSerializer]
public sealed class KeyedOrderV2
{
    [Id(7)]
    public int Revision { get; set; }

    [Id(0)]
    public Guid Id { get; set; }

    [Id(2)]
    public decimal Total { get; set; }

    [Id(5)]
    public List<KeyedLine> Lines { get; set; } = [];

    [Id(8)]
    public string? Warehouse { get; set; }

    [Id(9)]
    public bool Expedited { get; set; }
}

[GenerateSerializer]
public sealed class ProbeRecord
{
    [Id(0)]
    public int A { get; set; }

    [Id(1)]
    public string B { get; set; } = string.Empty;

    [Id(2)]
    public double C { get; set; }
}

[GenerateSerializer]
public sealed class ProbeRecordAdded
{
    [Id(0)]
    public int A { get; set; }

    [Id(1)]
    public string B { get; set; } = string.Empty;

    [Id(2)]
    public double C { get; set; }

    [Id(3)]
    public long D { get; set; }
}

[GenerateSerializer]
public sealed class ProbeRecordRemoved
{
    [Id(0)]
    public int A { get; set; }

    [Id(2)]
    public double C { get; set; }
}

[GenerateSerializer]
public sealed class ProbeRecordReordered
{
    [Id(2)]
    public double C { get; set; }

    [Id(0)]
    public int A { get; set; }

    [Id(1)]
    public string B { get; set; } = string.Empty;
}

[GenerateSerializer]
public class EventBase
{
    [Id(0)]
    public long Sequence { get; set; }

    [Id(1)]
    public DateTime AtUtc { get; set; }
}

[GenerateSerializer]
public sealed class TextEvent : EventBase
{
    [Id(0)]
    public string Text { get; set; } = string.Empty;

    [Id(1)]
    public string Source { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class NumericEvent : EventBase
{
    [Id(0)]
    public double Value { get; set; }

    [Id(1)]
    public string Unit { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class StateEvent : EventBase
{
    [Id(0)]
    public Severity From { get; set; }

    [Id(1)]
    public Severity To { get; set; }

    [Id(2)]
    public bool Automatic { get; set; }
}

[GenerateSerializer]
public sealed class LocationEvent : EventBase
{
    [Id(0)]
    public double Latitude { get; set; }

    [Id(1)]
    public double Longitude { get; set; }

    [Id(2)]
    public float Accuracy { get; set; }
}

[GenerateSerializer]
public sealed class BatchEvent : EventBase
{
    [Id(0)]
    public List<long> Ids { get; set; } = [];

    [Id(1)]
    public int Total { get; set; }
}

[GenerateSerializer]
public sealed class ErrorEvent : EventBase
{
    [Id(0)]
    public string Code { get; set; } = string.Empty;

    [Id(1)]
    public string Message { get; set; } = string.Empty;

    [Id(2)]
    public int Attempt { get; set; }
}

[GenerateSerializer]
public abstract class ProbeShape
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class ProbeCircle : ProbeShape
{
    [Id(0)]
    public double Radius { get; set; }
}

[GenerateSerializer]
public sealed class ProbeSquare : ProbeShape
{
    [Id(0)]
    public double Side { get; set; }
}
