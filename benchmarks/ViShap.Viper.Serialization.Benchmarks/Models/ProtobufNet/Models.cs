// The corpus models as protobuf-net consumes them: every type of the schema — the Viper
// models — under the same name with the same members, carrying the attributes this library needs, and
// the probe models of the plan's §7.5. ModelSchema.Failures holds them to the schema (FAIR-19).

using System.Collections.Immutable;
using System.Numerics;
using ProtoBuf;

namespace ViShap.Viper.Serialization.Benchmarks.Models.ProtobufNetVariant;

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

[ProtoContract]
public sealed class TinyFlat
{
    [ProtoMember(1)]
    public int Id { get; set; }

    [ProtoMember(2)]
    public long Sequence { get; set; }

    [ProtoMember(3)]
    public short Region { get; set; }

    [ProtoMember(4)]
    public byte Version { get; set; }

    [ProtoMember(5)]
    public bool Active { get; set; }

    [ProtoMember(6)]
    public double Score { get; set; }

    [ProtoMember(7)]
    public float Ratio { get; set; }

    [ProtoMember(8)]
    public decimal Amount { get; set; }

    [ProtoMember(9)]
    public Channel Channel { get; set; }

    [ProtoMember(10)]
    public string Label { get; set; } = string.Empty;

    [ProtoMember(11)]
    public Guid Reference { get; set; }

    [ProtoMember(12)]
    public DateTime CreatedUtc { get; set; }
}

[ProtoContract]
public sealed class Address
{
    [ProtoMember(1)]
    public string Line1 { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string? Line2 { get; set; }

    [ProtoMember(3)]
    public string City { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string PostalCode { get; set; } = string.Empty;

    [ProtoMember(5)]
    public string CountryCode { get; set; } = string.Empty;

    [ProtoMember(6)]
    public double Latitude { get; set; }

    [ProtoMember(7)]
    public double Longitude { get; set; }
}

[ProtoContract]
public sealed class Measurement
{
    [ProtoMember(1)]
    public string Metric { get; set; } = string.Empty;

    [ProtoMember(2)]
    public double Value { get; set; }

    [ProtoMember(3)]
    public Severity Severity { get; set; }

    [ProtoMember(4)]
    public DateTime TakenUtc { get; set; }

    [ProtoMember(5)]
    public bool Estimated { get; set; }
}

[ProtoContract]
public sealed class MediumObject
{
    [ProtoMember(1)]
    public Guid Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Description { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string? Note { get; set; }

    [ProtoMember(5)]
    public int Revision { get; set; }

    [ProtoMember(6)]
    public long ExternalId { get; set; }

    [ProtoMember(7)]
    public short Priority { get; set; }

    [ProtoMember(8)]
    public byte Flags { get; set; }

    [ProtoMember(9)]
    public bool Published { get; set; }

    [ProtoMember(10)]
    public bool Archived { get; set; }

    [ProtoMember(11)]
    public double Weight { get; set; }

    [ProtoMember(12)]
    public double Confidence { get; set; }

    [ProtoMember(13)]
    public float Scale { get; set; }

    [ProtoMember(14)]
    public decimal Price { get; set; }

    [ProtoMember(15)]
    public decimal Discount { get; set; }

    [ProtoMember(16)]
    public Channel Channel { get; set; }

    [ProtoMember(17)]
    public Severity Severity { get; set; }

    [ProtoMember(18)]
    public DateTime CreatedUtc { get; set; }

    [ProtoMember(19)]
    public DateTime UpdatedUtc { get; set; }

    [ProtoMember(20)]
    public DateTime? DeletedUtc { get; set; }

    [ProtoMember(21)]
    public TimeSpan Duration { get; set; }

    [ProtoMember(22)]
    public Guid OwnerId { get; set; }

    [ProtoMember(23)]
    public Guid? DelegateId { get; set; }

    [ProtoMember(24)]
    public int? Quota { get; set; }

    [ProtoMember(25)]
    public double? Threshold { get; set; }

    [ProtoMember(26)]
    public string Culture { get; set; } = string.Empty;

    [ProtoMember(27)]
    public string Slug { get; set; } = string.Empty;

    [ProtoMember(28)]
    public string Category { get; set; } = string.Empty;

    [ProtoMember(29, IsRequired = true)]
    public Address Primary { get; set; } = new();

    [ProtoMember(30)]
    public Address? Secondary { get; set; }

    [ProtoMember(31)]
    public List<Measurement> Measurements { get; set; } = [];

    [ProtoMember(32)]
    public List<string> Tags { get; set; } = [];

    [ProtoMember(33)]
    public Dictionary<string, string> Attributes { get; set; } = [];

    [ProtoMember(34)]
    public int[] Counters { get; set; } = [];
}

[ProtoContract]
public sealed class ScalarRecord
{
    [ProtoMember(1)]
    public int Id { get; set; }

    [ProtoMember(2)]
    public long Ticks { get; set; }

    [ProtoMember(3)]
    public double Value { get; set; }

    [ProtoMember(4)]
    public bool Valid { get; set; }

    [ProtoMember(5)]
    public string Code { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class DeepNode
{
    [ProtoMember(1)]
    public int Level { get; set; }

    [ProtoMember(2)]
    public string Label { get; set; } = string.Empty;

    [ProtoMember(3)]
    public long Payload { get; set; }

    [ProtoMember(4)]
    public DeepNode? Child { get; set; }
}

[ProtoContract]
public sealed class DagNode
{
    [ProtoMember(1)]
    public int Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public double Weight { get; set; }

    [ProtoMember(4)]
    public List<DagNode> Children { get; set; } = [];
}

[ProtoContract]
public sealed class CyclicNode
{
    [ProtoMember(1)]
    public int Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public CyclicNode? Parent { get; set; }

    [ProtoMember(4)]
    public List<CyclicNode> Children { get; set; } = [];
}

[ProtoContract]
public sealed class BlobEnvelope
{
    [ProtoMember(1)]
    public Guid Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public byte[] Content { get; set; } = [];
}

[ProtoContract]
public sealed class NumericArrays
{
    [ProtoMember(1)]
    public int[] Integers { get; set; } = [];

    [ProtoMember(2)]
    public long[] Longs { get; set; } = [];

    [ProtoMember(3)]
    public double[] Doubles { get; set; } = [];
}

[ProtoContract]
public sealed class NullSparse
{
    [ProtoMember(1)]
    public int Id { get; set; }

    [ProtoMember(2)]
    public string? A { get; set; }

    [ProtoMember(3)]
    public string? B { get; set; }

    [ProtoMember(4)]
    public string? C { get; set; }

    [ProtoMember(5)]
    public string? D { get; set; }

    [ProtoMember(6)]
    public string? E { get; set; }

    [ProtoMember(7)]
    public int? F { get; set; }

    [ProtoMember(8)]
    public int? G { get; set; }

    [ProtoMember(9)]
    public long? H { get; set; }

    [ProtoMember(10)]
    public double? I { get; set; }

    [ProtoMember(11)]
    public decimal? J { get; set; }

    [ProtoMember(12)]
    public Guid? K { get; set; }

    [ProtoMember(13)]
    public DateTime? L { get; set; }

    [ProtoMember(14)]
    public TimeSpan? M { get; set; }

    [ProtoMember(15)]
    public Address? N { get; set; }

    [ProtoMember(16)]
    public Address? O { get; set; }

    [ProtoMember(17)]
    public Measurement? P { get; set; }

    [ProtoMember(18)]
    public List<string>? Q { get; set; }

    [ProtoMember(19)]
    public List<int>? R { get; set; }

    [ProtoMember(20)]
    public Dictionary<string, string>? S { get; set; }

    [ProtoMember(21)]
    public int[]? T { get; set; }
}

[ProtoContract]
public sealed class WideObject
{
    [ProtoMember(1)]
    public string M001 { get; set; } = string.Empty;

    [ProtoMember(2)]
    public double M002 { get; set; }

    [ProtoMember(3)]
    public bool M003 { get; set; }

    [ProtoMember(4)]
    public long M004 { get; set; }

    [ProtoMember(5)]
    public decimal M005 { get; set; }

    [ProtoMember(6)]
    public Guid M006 { get; set; }

    [ProtoMember(7)]
    public DateTime M007 { get; set; }

    [ProtoMember(8)]
    public int M008 { get; set; }

    [ProtoMember(9)]
    public string M009 { get; set; } = string.Empty;

    [ProtoMember(10)]
    public double M010 { get; set; }

    [ProtoMember(11)]
    public bool M011 { get; set; }

    [ProtoMember(12)]
    public long M012 { get; set; }

    [ProtoMember(13)]
    public decimal M013 { get; set; }

    [ProtoMember(14)]
    public Guid M014 { get; set; }

    [ProtoMember(15)]
    public DateTime M015 { get; set; }

    [ProtoMember(16)]
    public int M016 { get; set; }

    [ProtoMember(17)]
    public string M017 { get; set; } = string.Empty;

    [ProtoMember(18)]
    public double M018 { get; set; }

    [ProtoMember(19)]
    public bool M019 { get; set; }

    [ProtoMember(20)]
    public long M020 { get; set; }

    [ProtoMember(21)]
    public decimal M021 { get; set; }

    [ProtoMember(22)]
    public Guid M022 { get; set; }

    [ProtoMember(23)]
    public DateTime M023 { get; set; }

    [ProtoMember(24)]
    public int M024 { get; set; }

    [ProtoMember(25)]
    public string M025 { get; set; } = string.Empty;

    [ProtoMember(26)]
    public double M026 { get; set; }

    [ProtoMember(27)]
    public bool M027 { get; set; }

    [ProtoMember(28)]
    public long M028 { get; set; }

    [ProtoMember(29)]
    public decimal M029 { get; set; }

    [ProtoMember(30)]
    public Guid M030 { get; set; }

    [ProtoMember(31)]
    public DateTime M031 { get; set; }

    [ProtoMember(32)]
    public int M032 { get; set; }

    [ProtoMember(33)]
    public string M033 { get; set; } = string.Empty;

    [ProtoMember(34)]
    public double M034 { get; set; }

    [ProtoMember(35)]
    public bool M035 { get; set; }

    [ProtoMember(36)]
    public long M036 { get; set; }

    [ProtoMember(37)]
    public decimal M037 { get; set; }

    [ProtoMember(38)]
    public Guid M038 { get; set; }

    [ProtoMember(39)]
    public DateTime M039 { get; set; }

    [ProtoMember(40)]
    public int M040 { get; set; }

    [ProtoMember(41)]
    public string M041 { get; set; } = string.Empty;

    [ProtoMember(42)]
    public double M042 { get; set; }

    [ProtoMember(43)]
    public bool M043 { get; set; }

    [ProtoMember(44)]
    public long M044 { get; set; }

    [ProtoMember(45)]
    public decimal M045 { get; set; }

    [ProtoMember(46)]
    public Guid M046 { get; set; }

    [ProtoMember(47)]
    public DateTime M047 { get; set; }

    [ProtoMember(48)]
    public int M048 { get; set; }

    [ProtoMember(49)]
    public string M049 { get; set; } = string.Empty;

    [ProtoMember(50)]
    public double M050 { get; set; }

    [ProtoMember(51)]
    public bool M051 { get; set; }

    [ProtoMember(52)]
    public long M052 { get; set; }

    [ProtoMember(53)]
    public decimal M053 { get; set; }

    [ProtoMember(54)]
    public Guid M054 { get; set; }

    [ProtoMember(55)]
    public DateTime M055 { get; set; }

    [ProtoMember(56)]
    public int M056 { get; set; }

    [ProtoMember(57)]
    public string M057 { get; set; } = string.Empty;

    [ProtoMember(58)]
    public double M058 { get; set; }

    [ProtoMember(59)]
    public bool M059 { get; set; }

    [ProtoMember(60)]
    public long M060 { get; set; }

    [ProtoMember(61)]
    public decimal M061 { get; set; }

    [ProtoMember(62)]
    public Guid M062 { get; set; }

    [ProtoMember(63)]
    public DateTime M063 { get; set; }

    [ProtoMember(64)]
    public int M064 { get; set; }

    [ProtoMember(65)]
    public string M065 { get; set; } = string.Empty;

    [ProtoMember(66)]
    public double M066 { get; set; }

    [ProtoMember(67)]
    public bool M067 { get; set; }

    [ProtoMember(68)]
    public long M068 { get; set; }

    [ProtoMember(69)]
    public decimal M069 { get; set; }

    [ProtoMember(70)]
    public Guid M070 { get; set; }

    [ProtoMember(71)]
    public DateTime M071 { get; set; }

    [ProtoMember(72)]
    public int M072 { get; set; }

    [ProtoMember(73)]
    public string M073 { get; set; } = string.Empty;

    [ProtoMember(74)]
    public double M074 { get; set; }

    [ProtoMember(75)]
    public bool M075 { get; set; }

    [ProtoMember(76)]
    public long M076 { get; set; }

    [ProtoMember(77)]
    public decimal M077 { get; set; }

    [ProtoMember(78)]
    public Guid M078 { get; set; }

    [ProtoMember(79)]
    public DateTime M079 { get; set; }

    [ProtoMember(80)]
    public int M080 { get; set; }

    [ProtoMember(81)]
    public string M081 { get; set; } = string.Empty;

    [ProtoMember(82)]
    public double M082 { get; set; }

    [ProtoMember(83)]
    public bool M083 { get; set; }

    [ProtoMember(84)]
    public long M084 { get; set; }

    [ProtoMember(85)]
    public decimal M085 { get; set; }

    [ProtoMember(86)]
    public Guid M086 { get; set; }

    [ProtoMember(87)]
    public DateTime M087 { get; set; }

    [ProtoMember(88)]
    public int M088 { get; set; }

    [ProtoMember(89)]
    public string M089 { get; set; } = string.Empty;

    [ProtoMember(90)]
    public double M090 { get; set; }

    [ProtoMember(91)]
    public bool M091 { get; set; }

    [ProtoMember(92)]
    public long M092 { get; set; }

    [ProtoMember(93)]
    public decimal M093 { get; set; }

    [ProtoMember(94)]
    public Guid M094 { get; set; }

    [ProtoMember(95)]
    public DateTime M095 { get; set; }

    [ProtoMember(96)]
    public int M096 { get; set; }

    [ProtoMember(97)]
    public string M097 { get; set; } = string.Empty;

    [ProtoMember(98)]
    public double M098 { get; set; }

    [ProtoMember(99)]
    public bool M099 { get; set; }

    [ProtoMember(100)]
    public long M100 { get; set; }

    [ProtoMember(101)]
    public decimal M101 { get; set; }

    [ProtoMember(102)]
    public Guid M102 { get; set; }

    [ProtoMember(103)]
    public DateTime M103 { get; set; }

    [ProtoMember(104)]
    public int M104 { get; set; }

    [ProtoMember(105)]
    public string M105 { get; set; } = string.Empty;

    [ProtoMember(106)]
    public double M106 { get; set; }

    [ProtoMember(107)]
    public bool M107 { get; set; }

    [ProtoMember(108)]
    public long M108 { get; set; }

    [ProtoMember(109)]
    public decimal M109 { get; set; }

    [ProtoMember(110)]
    public Guid M110 { get; set; }

    [ProtoMember(111)]
    public DateTime M111 { get; set; }

    [ProtoMember(112)]
    public int M112 { get; set; }

    [ProtoMember(113)]
    public string M113 { get; set; } = string.Empty;

    [ProtoMember(114)]
    public double M114 { get; set; }

    [ProtoMember(115)]
    public bool M115 { get; set; }

    [ProtoMember(116)]
    public long M116 { get; set; }

    [ProtoMember(117)]
    public decimal M117 { get; set; }

    [ProtoMember(118)]
    public Guid M118 { get; set; }

    [ProtoMember(119)]
    public DateTime M119 { get; set; }

    [ProtoMember(120)]
    public int M120 { get; set; }

    [ProtoMember(121)]
    public string M121 { get; set; } = string.Empty;

    [ProtoMember(122)]
    public double M122 { get; set; }

    [ProtoMember(123)]
    public bool M123 { get; set; }

    [ProtoMember(124)]
    public long M124 { get; set; }

    [ProtoMember(125)]
    public decimal M125 { get; set; }

    [ProtoMember(126)]
    public Guid M126 { get; set; }

    [ProtoMember(127)]
    public DateTime M127 { get; set; }

    [ProtoMember(128)]
    public int M128 { get; set; }

    [ProtoMember(129)]
    public string M129 { get; set; } = string.Empty;

    [ProtoMember(130)]
    public double M130 { get; set; }

    [ProtoMember(131)]
    public bool M131 { get; set; }

    [ProtoMember(132)]
    public long M132 { get; set; }

    [ProtoMember(133)]
    public decimal M133 { get; set; }

    [ProtoMember(134)]
    public Guid M134 { get; set; }

    [ProtoMember(135)]
    public DateTime M135 { get; set; }

    [ProtoMember(136)]
    public int M136 { get; set; }

    [ProtoMember(137)]
    public string M137 { get; set; } = string.Empty;

    [ProtoMember(138)]
    public double M138 { get; set; }

    [ProtoMember(139)]
    public bool M139 { get; set; }

    [ProtoMember(140)]
    public long M140 { get; set; }

    [ProtoMember(141)]
    public decimal M141 { get; set; }

    [ProtoMember(142)]
    public Guid M142 { get; set; }

    [ProtoMember(143)]
    public DateTime M143 { get; set; }

    [ProtoMember(144)]
    public int M144 { get; set; }

    [ProtoMember(145)]
    public string M145 { get; set; } = string.Empty;

    [ProtoMember(146)]
    public double M146 { get; set; }

    [ProtoMember(147)]
    public bool M147 { get; set; }

    [ProtoMember(148)]
    public long M148 { get; set; }

    [ProtoMember(149)]
    public decimal M149 { get; set; }

    [ProtoMember(150)]
    public Guid M150 { get; set; }

    [ProtoMember(151)]
    public DateTime M151 { get; set; }

    [ProtoMember(152)]
    public int M152 { get; set; }

    [ProtoMember(153)]
    public string M153 { get; set; } = string.Empty;

    [ProtoMember(154)]
    public double M154 { get; set; }

    [ProtoMember(155)]
    public bool M155 { get; set; }

    [ProtoMember(156)]
    public long M156 { get; set; }

    [ProtoMember(157)]
    public decimal M157 { get; set; }

    [ProtoMember(158)]
    public Guid M158 { get; set; }

    [ProtoMember(159)]
    public DateTime M159 { get; set; }

    [ProtoMember(160)]
    public int M160 { get; set; }

    [ProtoMember(161)]
    public string M161 { get; set; } = string.Empty;

    [ProtoMember(162)]
    public double M162 { get; set; }

    [ProtoMember(163)]
    public bool M163 { get; set; }

    [ProtoMember(164)]
    public long M164 { get; set; }

    [ProtoMember(165)]
    public decimal M165 { get; set; }

    [ProtoMember(166)]
    public Guid M166 { get; set; }

    [ProtoMember(167)]
    public DateTime M167 { get; set; }

    [ProtoMember(168)]
    public int M168 { get; set; }

    [ProtoMember(169)]
    public string M169 { get; set; } = string.Empty;

    [ProtoMember(170)]
    public double M170 { get; set; }

    [ProtoMember(171)]
    public bool M171 { get; set; }

    [ProtoMember(172)]
    public long M172 { get; set; }

    [ProtoMember(173)]
    public decimal M173 { get; set; }

    [ProtoMember(174)]
    public Guid M174 { get; set; }

    [ProtoMember(175)]
    public DateTime M175 { get; set; }

    [ProtoMember(176)]
    public int M176 { get; set; }

    [ProtoMember(177)]
    public string M177 { get; set; } = string.Empty;

    [ProtoMember(178)]
    public double M178 { get; set; }

    [ProtoMember(179)]
    public bool M179 { get; set; }

    [ProtoMember(180)]
    public long M180 { get; set; }

    [ProtoMember(181)]
    public decimal M181 { get; set; }

    [ProtoMember(182)]
    public Guid M182 { get; set; }

    [ProtoMember(183)]
    public DateTime M183 { get; set; }

    [ProtoMember(184)]
    public int M184 { get; set; }

    [ProtoMember(185)]
    public string M185 { get; set; } = string.Empty;

    [ProtoMember(186)]
    public double M186 { get; set; }

    [ProtoMember(187)]
    public bool M187 { get; set; }

    [ProtoMember(188)]
    public long M188 { get; set; }

    [ProtoMember(189)]
    public decimal M189 { get; set; }

    [ProtoMember(190)]
    public Guid M190 { get; set; }

    [ProtoMember(191)]
    public DateTime M191 { get; set; }

    [ProtoMember(192)]
    public int M192 { get; set; }

    [ProtoMember(193)]
    public string M193 { get; set; } = string.Empty;

    [ProtoMember(194)]
    public double M194 { get; set; }

    [ProtoMember(195)]
    public bool M195 { get; set; }

    [ProtoMember(196)]
    public long M196 { get; set; }

    [ProtoMember(197)]
    public decimal M197 { get; set; }

    [ProtoMember(198)]
    public Guid M198 { get; set; }

    [ProtoMember(199)]
    public DateTime M199 { get; set; }

    [ProtoMember(200)]
    public int M200 { get; set; }
}

[ProtoContract]
public sealed class CollectionZoo
{
    [ProtoMember(1)]
    public int[] Array { get; set; } = [];

    [ProtoMember(2)]
    public List<string> List { get; set; } = [];

    [ProtoMember(3)]
    public HashSet<int> Set { get; set; } = [];

    [ProtoMember(4)]
    public SortedSet<int> SortedSet { get; set; } = [];

    [ProtoMember(5, IsRequired = true)]
    public LinkedList<string> Linked { get; set; } = new();

    [ProtoMember(6, IsRequired = true)]
    public Stack<int> Stack { get; set; } = new();

    [ProtoMember(7, IsRequired = true)]
    public Queue<int> Queue { get; set; } = new();

    [ProtoMember(8)]
    public Dictionary<string, int> Dictionary { get; set; } = [];

    [ProtoMember(9)]
    public SortedDictionary<string, int> SortedDictionary { get; set; } = [];

    [ProtoMember(10)]
    public ImmutableArray<int> ImmutableArray { get; set; } = [];

    [ProtoMember(11)]
    public ImmutableList<string> ImmutableList { get; set; } = ImmutableList<string>.Empty;

    [ProtoMember(12)]
    public ImmutableDictionary<int, string> ImmutableDictionary { get; set; } = System.Collections.Immutable.ImmutableDictionary<int, string>.Empty;

    [ProtoMember(13)]
    public KeyValuePair<string, int> Pair { get; set; }

    [ProtoMember(14)]
    public (int Number, string Text) Tuple { get; set; }

    [ProtoMember(15)]
    public int[,] Rectangular { get; set; } = new int[0, 0];
}

[ProtoContract]
public sealed class TimeAndNumerics
{
    [ProtoMember(1)]
    public DateTime Utc { get; set; }

    [ProtoMember(2)]
    public DateTime Local { get; set; }

    [ProtoMember(3)]
    public DateTimeOffset Offset { get; set; }

    [ProtoMember(4)]
    public TimeSpan Elapsed { get; set; }

    [ProtoMember(5)]
    public DateOnly Date { get; set; }

    [ProtoMember(6)]
    public TimeOnly Time { get; set; }

    [ProtoMember(7)]
    public decimal Money { get; set; }

    [ProtoMember(8)]
    public Int128 Wide { get; set; }

    [ProtoMember(9)]
    public UInt128 WideUnsigned { get; set; }

    [ProtoMember(10)]
    public BigInteger Big { get; set; }

    [ProtoMember(11)]
    public Half Small { get; set; }

    [ProtoMember(12)]
    public Complex Complex { get; set; }

    [ProtoMember(13)]
    public Vector2 Vector2 { get; set; }

    [ProtoMember(14)]
    public Vector3 Vector3 { get; set; }

    [ProtoMember(15)]
    public Vector4 Vector4 { get; set; }

    [ProtoMember(16)]
    public Quaternion Quaternion { get; set; }

    [ProtoMember(17)]
    public Matrix3x2 Matrix3X2 { get; set; }

    [ProtoMember(18)]
    public Matrix4x4 Matrix4X4 { get; set; }

    [ProtoMember(19)]
    public Guid Guid { get; set; }

    [ProtoMember(20, IsRequired = true)]
    public Version Version { get; set; } = new(1, 0);

    [ProtoMember(21, IsRequired = true)]
    public Uri Uri { get; set; } = new("https://example.invalid/");
}

[ProtoContract]
public sealed class ProbeLeaf
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class ProbePair
{
    [ProtoMember(1)]
    public ProbeLeaf? Left { get; set; }

    [ProtoMember(2)]
    public ProbeLeaf? Right { get; set; }
}

[ProtoContract]
public sealed class ProbeCycle
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(2)]
    public ProbeCycle? Next { get; set; }
}

[ProtoContract]
public sealed class ProbeDeep
{
    [ProtoMember(1)]
    public int Level { get; set; }

    [ProtoMember(2)]
    public ProbeDeep? Next { get; set; }
}

[ProtoContract]
public sealed class KeyedOrder
{
    [ProtoMember(1)]
    public Guid Id { get; set; }

    [ProtoMember(2)]
    public string Customer { get; set; } = string.Empty;

    [ProtoMember(3)]
    public decimal Total { get; set; }

    [ProtoMember(4)]
    public DateTime PlacedUtc { get; set; }

    [ProtoMember(5)]
    public Channel Channel { get; set; }

    [ProtoMember(6)]
    public List<KeyedLine> Lines { get; set; } = [];

    [ProtoMember(7)]
    public string? Note { get; set; }

    [ProtoMember(8)]
    public int Revision { get; set; }
}

[ProtoContract]
public sealed class KeyedLine
{
    [ProtoMember(1)]
    public string Sku { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Quantity { get; set; }

    [ProtoMember(3)]
    public decimal UnitPrice { get; set; }

    [ProtoMember(4)]
    public string? Comment { get; set; }
}

[ProtoContract]
public sealed class KeyedOrderV2
{
    [ProtoMember(8)]
    public int Revision { get; set; }

    [ProtoMember(1)]
    public Guid Id { get; set; }

    [ProtoMember(3)]
    public decimal Total { get; set; }

    [ProtoMember(6)]
    public List<KeyedLine> Lines { get; set; } = [];

    [ProtoMember(9)]
    public string? Warehouse { get; set; }

    [ProtoMember(10)]
    public bool Expedited { get; set; }
}

[ProtoContract]
public sealed class ProbeRecord
{
    [ProtoMember(1)]
    public int A { get; set; }

    [ProtoMember(2)]
    public string B { get; set; } = string.Empty;

    [ProtoMember(3)]
    public double C { get; set; }
}

[ProtoContract]
public sealed class ProbeRecordAdded
{
    [ProtoMember(1)]
    public int A { get; set; }

    [ProtoMember(2)]
    public string B { get; set; } = string.Empty;

    [ProtoMember(3)]
    public double C { get; set; }

    [ProtoMember(4)]
    public long D { get; set; }
}

[ProtoContract]
public sealed class ProbeRecordRemoved
{
    [ProtoMember(1)]
    public int A { get; set; }

    [ProtoMember(3)]
    public double C { get; set; }
}

[ProtoContract]
public sealed class ProbeRecordReordered
{
    [ProtoMember(3)]
    public double C { get; set; }

    [ProtoMember(1)]
    public int A { get; set; }

    [ProtoMember(2)]
    public string B { get; set; } = string.Empty;
}

[ProtoInclude(101, typeof(TextEvent))]
[ProtoInclude(102, typeof(NumericEvent))]
[ProtoInclude(103, typeof(StateEvent))]
[ProtoInclude(104, typeof(LocationEvent))]
[ProtoInclude(105, typeof(BatchEvent))]
[ProtoInclude(106, typeof(ErrorEvent))]
[ProtoContract]
public class EventBase
{
    [ProtoMember(1)]
    public long Sequence { get; set; }

    [ProtoMember(2)]
    public DateTime AtUtc { get; set; }
}

[ProtoContract]
public sealed class TextEvent : EventBase
{
    [ProtoMember(1)]
    public string Text { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string Source { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class NumericEvent : EventBase
{
    [ProtoMember(1)]
    public double Value { get; set; }

    [ProtoMember(2)]
    public string Unit { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class StateEvent : EventBase
{
    [ProtoMember(1)]
    public Severity From { get; set; }

    [ProtoMember(2)]
    public Severity To { get; set; }

    [ProtoMember(3)]
    public bool Automatic { get; set; }
}

[ProtoContract]
public sealed class LocationEvent : EventBase
{
    [ProtoMember(1)]
    public double Latitude { get; set; }

    [ProtoMember(2)]
    public double Longitude { get; set; }

    [ProtoMember(3)]
    public float Accuracy { get; set; }
}

[ProtoContract]
public sealed class BatchEvent : EventBase
{
    [ProtoMember(1)]
    public List<long> Ids { get; set; } = [];

    [ProtoMember(2)]
    public int Total { get; set; }
}

[ProtoContract]
public sealed class ErrorEvent : EventBase
{
    [ProtoMember(1)]
    public string Code { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string Message { get; set; } = string.Empty;

    [ProtoMember(3)]
    public int Attempt { get; set; }
}

[ProtoInclude(101, typeof(ProbeCircle))]
[ProtoInclude(102, typeof(ProbeSquare))]
[ProtoContract]
public abstract class ProbeShape
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;
}

[ProtoContract]
public sealed class ProbeCircle : ProbeShape
{
    [ProtoMember(1)]
    public double Radius { get; set; }
}

[ProtoContract]
public sealed class ProbeSquare : ProbeShape
{
    [ProtoMember(1)]
    public double Side { get; set; }
}
