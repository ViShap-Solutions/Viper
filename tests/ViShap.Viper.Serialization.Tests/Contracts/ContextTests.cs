using ViShap.Viper.Contracts;
using ViShap.Viper.Engine;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTX-08: the conformance suite run against contracts written by hand and supplied through a
/// <see cref="BinarySerializerContext"/>: each case reaches its contract through the options the
/// context was given to, which is where the engine finds it.
/// </summary>
public sealed class ContextContractConformanceTests : ContractConformance
{
    private static readonly BinarySerializerOptions Options =
        BinarySerializerOptions.Configure().WithContracts(HandWrittenContracts.Default).Build();

    internal override TypeContract<T> ContractOf<T>() =>
        Options.ContractSet!.Find<T>() ?? throw new InvalidOperationException($"No contract for '{typeof(T)}'.");
}

/// <summary>
/// Pins CTX-01…CTX-07: a context supplies contracts through the options, the engine uses them before
/// reflection — for a declared type, in the polymorphic slot and when it decides how a value carries
/// its null — and writes the bytes reflection writes; <c>RequireGeneratedContracts</c> turns the
/// fallback into a configuration error; a context and a description refuse what is malformed; and a
/// copy of a member writer or reader is refused instead of distorting the payload.
/// </summary>
public class ContextTests
{
    private static readonly BinarySerializer Reflected = new();

    private static readonly BinarySerializer WithContext = new(
        BinarySerializerOptions.Configure().WithContracts(HandWrittenContracts.Default).Build());

    // --- CTX-01: the context's contracts write what reflection writes -------------------------------

    public static TheoryData<int, bool> Configurations() => new()
    {
        { 1, false },
        { 1, true },
        { 0, false }
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void Serialize_ThroughTheContext_WritesTheBytesReflectionWrites(int version, bool references)
    {
        var reflected = new BinarySerializer(Build(version, references, context: null));
        var generated = new BinarySerializer(Build(version, references, HandWrittenContracts.Default));

        var keyed = new ConformanceKeyed { Id = 7, Name = "Ada", Big = 1 };
        keyed.SetCode(-2);
        var derived = new ConformanceDerived { Alpha = 1, Mike = 2, Zulu = 3 };
        derived.SetHidden(4);

        AssertSameBytes(reflected, generated, keyed);
        AssertSameBytes(reflected, generated, derived);
        AssertSameBytes(reflected, generated, new ConformanceKeyedDerived { First = 1, Second = 2, Third = 3 });
        AssertSameBytes(reflected, generated, new ConformanceOverridden { Value = 1, Other = 2 });
        AssertSameBytes(reflected, generated, new ConformancePoint { X = 1, Y = -1 });
        AssertSameBytes(reflected, generated, new ConformanceKeyedPoint { X = 5, Tag = "t" });
        AssertSameBytes<ConformanceShape>(reflected, generated, new ConformanceCircle { Label = "c", Radius = 2 });
        AssertSameBytes<ConformanceKeyed?>(reflected, generated, null);
        AssertSameBytes(reflected, generated, new List<ConformanceKeyed?> { keyed, null, keyed });
    }

    [Fact]
    public void Deserialize_ThroughTheContext_ReadsWhatReflectionWrote()
    {
        var keyed = new ConformanceKeyed { Id = 7, Name = "Ada", Defaulted = 9, Big = 1 };
        keyed.SetCode(-2);

        var read = WithContext.Deserialize<ConformanceKeyed>(Reflected.Serialize(keyed))!;

        Assert.Equal((7, "Ada", 9, 1L, (short)-2), (read.Id, read.Name, read.Defaulted, read.Big, read.GetCode()));
    }

    // --- CTX-02: the engine asks the context first ---------------------------------------------------

    [Fact]
    public void Serialize_ATypeTheContextHolds_GoesThroughItsContract()
    {
        var context = new CountingContext();
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

        var read = serializer.Deserialize<CountedPair>(serializer.Serialize(new CountedPair { A = 3, B = "b" }))!;

        Assert.Equal((3, "b"), (read.A, read.B));
        Assert.Equal((1, 1, 1), (context.Contract.Writes, context.Contract.Reads, context.Contract.Creates));
    }

    [Fact]
    public void Serialize_ARuntimeTypeInThePolymorphicSlot_GoesThroughTheContextsContract()
    {
        var context = new CountingContext();
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

        var read = serializer.Deserialize<CountedBase>(serializer.Serialize<CountedBase>(new CountedPair { A = 1 }));

        Assert.IsType<CountedPair>(read);
        Assert.Equal((1, 1), (context.Contract.Writes, context.Contract.Reads));
    }

    [Fact]
    public void Serialize_TheSameTypeWithAndWithoutAContext_UsesEachConfigurationsContract()
    {
        var context = new CountingContext();
        var withContext = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(context).Build());

        Reflected.Serialize(new CountedPair { A = 1 });
        withContext.Serialize(new CountedPair { A = 1 });
        Reflected.Serialize(new CountedPair { A = 1 });

        Assert.Equal(1, context.Contract.Writes);
    }

    // --- CTX-03: the null follows the layout of the contract in force ------------------------------

    [Fact]
    public void Serialize_AContractWhoseLayoutDiffersFromReflection_CarriesNullTheWayItsLayoutDoes()
    {
        // Reflection describes CountedPair as positional — a null flag; the context's contract is
        // keyed, so a null pair is the zero of its field count.
        var keyed = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithVersion(0).AllowV0Fallback().WithContracts(new KeyedPairContext()).Build());
        var positional = new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).Build());

        var holder = new PairHolder { Pair = new CountedPair { A = 1, B = "x" } };

        Assert.Equal<byte[]>([0x00], positional.Serialize<CountedPair?>(null));
        Assert.Equal<byte[]>([0x00], keyed.Serialize<CountedPair?>(null));
        Assert.Equal<byte[]>(
            [0x03, 0x01, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x02, 0x00, 0x00, 0x00, 0x02, 0x78],
            keyed.Serialize(holder.Pair));
        var read = keyed.Deserialize<PairHolder>(keyed.Serialize(holder))!.Pair!;
        Assert.Equal((1, "x"), (read.A, read.B));
    }

    // --- CTX-04: RequireGeneratedContracts --------------------------------------------------------

    [Fact]
    public void Serialize_RequiringContracts_ATypeTheContextLacks_ThrowsConfigurationNamingIt()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(HandWrittenContracts.Default)
            .RequireGeneratedContracts()
            .Build());

        var ex = AssertEx.Throws<BinaryConfigurationException>(
            nameof(CountedPair), () => serializer.Serialize(new CountedPair()));
        Assert.Contains("RequireGeneratedContracts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RequiringContracts_AUnionArmTheContextLacks_ThrowsConfigurationNamingIt()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(HandWrittenContracts.Default)
            .RequireGeneratedContracts()
            .Build());

        byte[] bytes = Reflected.Serialize<CountedBase>(new CountedPair { A = 1 });

        AssertEx.Throws<BinaryConfigurationException>(
            nameof(CountedPair), () => serializer.Deserialize<CountedBase>(bytes));
    }

    [Fact]
    public void Serialize_RequiringContracts_EveryTypeInTheContext_RoundTrips()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(HandWrittenContracts.Default)
            .RequireGeneratedContracts()
            .Build());

        var read = serializer.Deserialize<ConformanceShape>(
            serializer.Serialize<ConformanceShape>(new ConformanceSquare { Label = "s", Side = 4 }));

        Assert.Equal(("s", 4), (read!.Label, Assert.IsType<ConformanceSquare>(read).Side));
    }

    [Fact]
    public void Build_RequiringContractsWithoutAContext_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "WithContracts", () => BinarySerializerOptions.Configure().RequireGeneratedContracts().Build());
    }

    [Fact]
    public void Build_WithAContext_ExposesItAndThePolicyOnTheOptions()
    {
        var options = BinarySerializerOptions.Configure()
            .WithContracts(HandWrittenContracts.Default)
            .RequireGeneratedContracts()
            .Build();

        Assert.Same(HandWrittenContracts.Default, options.Contracts);
        Assert.True(options.RequireGeneratedContracts);
        Assert.Null(BinarySerializerOptions.Default.Contracts);
        Assert.False(BinarySerializerOptions.Default.RequireGeneratedContracts);
    }

    [Fact]
    public void WithContracts_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => BinarySerializerOptions.Configure().WithContracts(null!));
    }

    // --- CTX-05: a context is filled once, one contract per type ------------------------------------

    [Fact]
    public void Add_ASecondContractForAType_ThrowsConfigurationNamingIt()
    {
        AssertEx.Throws<BinaryConfigurationException>(nameof(CountedPair), () => new TwiceContext());
    }

    [Fact]
    public void Add_AfterOptionsWereBuilt_ThrowsConfiguration()
    {
        var context = new LateContext();
        BinarySerializerOptions.Configure().WithContracts(context).Build();

        Assert.Throws<BinaryConfigurationException>(context.AddLate);
    }

    [Fact]
    public void Add_Null_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NullContext());
    }

    // --- CTX-06: a description refuses what the engine could not check against ----------------------

    [Fact]
    public void Description_MalformedArguments_ThrowConfiguration()
    {
        Assert.Throws<BinaryConfigurationException>(() => new MemberDescription("", typeof(int), null));
        Assert.Throws<BinaryConfigurationException>(() => new MemberDescription(null!, typeof(int), null));
        Assert.Throws<BinaryConfigurationException>(() => new MemberDescription("A", null!, null));
        AssertEx.Throws<BinaryConfigurationException>("-1", () => new MemberDescription("A", typeof(int), -1));
    }

    [Fact]
    public void Contract_AKeyUnderAPositionalLayout_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "'A'", () => new DescribedContract(MemberLayout.Positional, [new("A", typeof(int), 1)]));
    }

    [Fact]
    public void Contract_AKeyedMemberWithoutAKey_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "'B'", () => new DescribedContract(MemberLayout.Keyed, [new("A", typeof(int), 1), new("B", typeof(string), null)]));
    }

    [Fact]
    public void Contract_KeysOutOfOrderOrRepeated_ThrowConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "key 1", () => new DescribedContract(MemberLayout.Keyed, [new("A", typeof(int), 2), new("B", typeof(string), 1)]));
        AssertEx.Throws<BinaryConfigurationException>(
            "key 2", () => new DescribedContract(MemberLayout.Keyed, [new("A", typeof(int), 2), new("B", typeof(string), 2)]));
    }

    [Fact]
    public void Contract_NullMembersOrAnUndefinedLayout_ThrowConfiguration()
    {
        Assert.Throws<BinaryConfigurationException>(() => new DescribedContract(MemberLayout.Positional, null!));
        Assert.Throws<BinaryConfigurationException>(() => new DescribedContract(MemberLayout.Positional, [null!]));
        Assert.Throws<BinaryConfigurationException>(() => new DescribedContract((MemberLayout)7, []));
    }

    [Fact]
    public void Contract_TheDescriptionArray_IsCopied()
    {
        MemberDescription[] members = [new("A", typeof(int), null)];
        var contract = new DescribedContract(MemberLayout.Positional, members);

        members[0] = new MemberDescription("Z", typeof(long), null);

        Assert.Equal("A", Assert.Single(contract.Members).Name);
    }

    // --- CTX-07: a copy of a member writer or reader is refused -------------------------------------

    [Fact]
    public void Serialize_AContractWritingThroughACopy_ThrowsTypeAndWritesNothing()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(new MisbehavingContext(Misbehaviour.WriteThroughCopy)).Build());
        var destination = new MemoryStream();

        AssertEx.Throws<BinaryTypeException>(
            "copy", () => serializer.Serialize(destination, new CountedPair { A = 1, B = "b" }));
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public void Serialize_AContractReplacingItsWriterWithDefault_ThrowsType()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(new MisbehavingContext(Misbehaviour.ReplaceWriter)).Build());

        AssertEx.Throws<BinaryTypeException>(
            "MemberWriter", () => serializer.Serialize(new CountedPair { A = 1, B = "b" }));
    }

    [Fact]
    public void Deserialize_AContractReadingThroughACopy_ThrowsType()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(new MisbehavingContext(Misbehaviour.ReadThroughCopy)).Build());
        byte[] bytes = Reflected.Serialize(new CountedPair { A = 1, B = "b" });

        AssertEx.Throws<BinaryTypeException>("copy", () => serializer.Deserialize<CountedPair>(bytes));
    }

    [Fact]
    public void Deserialize_AKeyedContractReadingAFieldThroughACopy_ThrowsType()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(new MisbehavingContext(Misbehaviour.ReadFieldThroughCopy)).Build());
        byte[] bytes = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithContracts(new KeyedPairContext()).Build()).Serialize(new CountedPair { A = 1, B = "b" });

        AssertEx.Throws<BinaryTypeException>("copy", () => serializer.Deserialize<CountedPair>(bytes));
    }

    [Fact]
    public void Member_OnADefaultWriterOrReader_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>("MemberWriter", () =>
        {
            MemberWriter writer = default;
            writer.Member(1);
        });

        AssertEx.Throws<BinaryTypeException>("MemberReader", () =>
        {
            MemberReader reader = default;
            _ = reader.Member<int>();
        });
    }

    // --- helpers and doubles -------------------------------------------------------------------

    private static BinarySerializerOptions Build(int version, bool references, BinarySerializerContext? context)
    {
        var builder = BinarySerializerOptions.Configure()
            .WithVersion(version)
            .AllowV0Fallback(version == 0)
            .PreserveReferences(references);
        return context is null ? builder.Build() : builder.WithContracts(context).Build();
    }

    private static void AssertSameBytes<T>(BinarySerializer reflected, BinarySerializer generated, T value)
    {
        byte[] expected = reflected.Serialize(value);

        Assert.Equal(expected, generated.Serialize(value));
        Assert.Equal(expected, generated.Serialize(generated.Deserialize<T>(expected)));
    }

    /// <summary>A union base with one arm, so the arm is met in the polymorphic slot.</summary>
    [BinaryUnion(1, typeof(CountedPair))]
    public abstract class CountedBase;

    public sealed class CountedPair : CountedBase
    {
        public int A { get; set; }

        public string? B { get; set; }
    }

    public sealed class PairHolder
    {
        public CountedPair? Pair { get; set; }
    }

    private static readonly MemberDescription[] PositionalPair =
        [new("A", typeof(int), null), new("B", typeof(string), null)];

    private static readonly MemberDescription[] KeyedPair =
        [new("A", typeof(int), 1), new("B", typeof(string), 2)];

    /// <summary>The positional contract reflection would build for <see cref="CountedPair"/>, counting its calls.</summary>
    private sealed class CountingContract() : TypeContract<CountedPair>(MemberLayout.Positional, PositionalPair, canBeConstructed: true)
    {
        public int Writes, Reads, Creates;

        public override CountedPair Create()
        {
            Creates++;
            return new CountedPair();
        }

        public override void Write(ref MemberWriter writer, in CountedPair value)
        {
            Writes++;
            writer.Member(value.A);
            writer.Member(value.B);
        }

        public override void ReadPositional(ref MemberReader reader, ref CountedPair value)
        {
            Reads++;
            value.A = reader.Member<int>();
            value.B = reader.Member<string>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref CountedPair value) =>
            throw new NotSupportedException();
    }

    private sealed class CountingContext : BinarySerializerContext
    {
        public CountingContext() => Add(Contract);

        public CountingContract Contract { get; } = new();
    }

    /// <summary>A keyed contract for a type reflection describes as positional.</summary>
    private sealed class KeyedPairContract() : TypeContract<CountedPair>(MemberLayout.Keyed, KeyedPair, canBeConstructed: true)
    {
        public override CountedPair Create() => new();

        public override void Write(ref MemberWriter writer, in CountedPair value)
        {
            writer.Member(1, value.A);
            writer.Member(2, value.B);
        }

        public override void ReadPositional(ref MemberReader reader, ref CountedPair value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref CountedPair value)
        {
            switch (key)
            {
                case 1: value.A = reader.Member<int>(); return true;
                case 2: value.B = reader.Member<string>(); return true;
                default: return false;
            }
        }
    }

    private sealed class KeyedPairContext : BinarySerializerContext
    {
        public KeyedPairContext() => Add(new KeyedPairContract());
    }

    private sealed class TwiceContext : BinarySerializerContext
    {
        public TwiceContext()
        {
            Add(new KeyedPairContract());
            Add(new CountingContract());
        }
    }

    private sealed class LateContext : BinarySerializerContext
    {
        public void AddLate() => Add(new KeyedPairContract());
    }

    private sealed class NullContext : BinarySerializerContext
    {
        public NullContext() => Add<CountedPair>(null!);
    }

    private sealed class DescribedContract(MemberLayout layout, MemberDescription[] members)
        : TypeContract<CountedPair>(layout, members, canBeConstructed: true)
    {
        public override CountedPair Create() => new();

        public override void Write(ref MemberWriter writer, in CountedPair value) { }

        public override void ReadPositional(ref MemberReader reader, ref CountedPair value) { }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref CountedPair value) => false;
    }

    public enum Misbehaviour
    {
        WriteThroughCopy,
        ReplaceWriter,
        ReadThroughCopy,
        ReadFieldThroughCopy
    }

    /// <summary>A contract that hands its writer or reader on by value, or replaces it, as a careless author might.</summary>
    private sealed class MisbehavingContract(Misbehaviour misbehaviour) : TypeContract<CountedPair>(
        misbehaviour == Misbehaviour.ReadFieldThroughCopy ? MemberLayout.Keyed : MemberLayout.Positional,
        misbehaviour == Misbehaviour.ReadFieldThroughCopy ? KeyedPair : PositionalPair,
        canBeConstructed: true)
    {
        public override CountedPair Create() => new();

        public override void Write(ref MemberWriter writer, in CountedPair value)
        {
            switch (misbehaviour)
            {
                case Misbehaviour.WriteThroughCopy:
                    WriteA(writer, value.A);
                    writer.Member(value.B);
                    break;

                case Misbehaviour.ReplaceWriter:
                    writer.Member(value.A);
                    writer.Member(value.B);
                    writer = default;
                    break;

                default:
                    writer.Member(value.A);
                    writer.Member(value.B);
                    break;
            }
        }

        public override void ReadPositional(ref MemberReader reader, ref CountedPair value)
        {
            if (misbehaviour == Misbehaviour.ReadThroughCopy)
                value.A = ReadA(reader);
            else
                value.A = reader.Member<int>();

            value.B = reader.Member<string>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref CountedPair value)
        {
            if (key == 1)
            {
                value.A = ReadA(reader);
                return true;
            }

            value.B = reader.Member<string>();
            return true;
        }

        private static void WriteA(MemberWriter writer, int a) => writer.Member(a);

        private static int ReadA(MemberReader reader) => reader.Member<int>();
    }

    private sealed class MisbehavingContext : BinarySerializerContext
    {
        public MisbehavingContext(Misbehaviour misbehaviour) => Add(new MisbehavingContract(misbehaviour));
    }
}
