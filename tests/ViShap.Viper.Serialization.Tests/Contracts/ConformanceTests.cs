using ViShap.Viper.Contracts;
using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CONF-01…CONF-07, the contract conformance suite: for every object shape — positional, keyed,
/// inherited, shadowed, overridden, union, struct — the layout, the member order, the member types,
/// the keys, the construction, and the exact bytes the engine produces around a contract's calls and
/// reads back through them.
/// <para>
/// A type contract is the unit a generated contract replaces, so every case reaches the contract
/// through <see cref="ContractOf{T}"/> alone and drives it through the engine's own entries for a
/// contract. A class that derives from this one and returns another implementation of
/// <see cref="TypeContract{T}"/> runs the same cases against it, and passes only if it produces the
/// description and the bytes the reflected contract does.
/// </para>
/// </summary>
public abstract class ContractConformance
{
    /// <summary>The contract under test for <typeparamref name="T"/>.</summary>
    internal abstract TypeContract<T> ContractOf<T>();

    // --- CONF-01: positional ---------------------------------------------------------------------

    [Fact]
    public void Positional_Description_OrdersExplicitThenOrdinalAndIncludesOnlyTheValue()
    {
        var contract = ContractOf<ConformancePositional>();

        Assert.Equal(MemberLayout.Positional, contract.Layout);
        Assert.True(contract.CanBeConstructed);
        Assert.Equal(
            [
                ("Id", typeof(int), null),
                ("Flag", typeof(bool), null),
                ("Name", typeof(string), null),
                ("Origin", typeof(ConformancePoint), null),
                ("_ratio", typeof(double), (int?)null)
            ],
            Describe(contract));
    }

    [Fact]
    public void Positional_Write_EachMemberInPlanOrder()
    {
        Assert.Equal<byte[]>(
        [
            0x07, 0x00, 0x00, 0x00,                          // Id
            0x01,                                            // Flag
            0x04, 0x41, 0x64, 0x61,                          // Name "Ada"
            0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF,  // Origin, a struct: no null flag
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE0, 0x3F   // _ratio 0.5
        ], Write(ContractOf<ConformancePositional>(), Positional()));
    }

    [Fact]
    public void Positional_Read_RestoresEveryMemberFromItsBytes()
    {
        var contract = ContractOf<ConformancePositional>();
        var read = Read(contract, Write(contract, Positional()));

        Assert.Equal(7, read.Id);
        Assert.True(read.Flag);
        Assert.Equal("Ada", read.Name);
        Assert.Equal(new ConformancePoint { X = 1, Y = -1 }, read.Origin);
        Assert.Equal(0.5, read.GetRatio());
        Assert.Equal(0, read.Skipped);
    }

    [Fact]
    public void Positional_Create_ReturnsAFreshInstanceEachTime()
    {
        var contract = ContractOf<ConformancePositional>();
        var first = contract.Create();

        Assert.NotNull(first);
        Assert.NotSame(first, contract.Create());
    }

    // --- CONF-02: keyed --------------------------------------------------------------------------

    [Fact]
    public void Keyed_Create_RunsTheParameterlessConstructor()
    {
        Assert.Equal(42, ContractOf<ConformanceKeyed>().Create().Defaulted);
    }

    [Fact]
    public void Keyed_Description_OrdersByKeyAndIncludesNonPublicKeyedMembers()
    {
        var contract = ContractOf<ConformanceKeyed>();

        Assert.Equal(MemberLayout.Keyed, contract.Layout);
        Assert.True(contract.CanBeConstructed);
        Assert.Equal(
            [
                ("Id", typeof(int), 1),
                ("_code", typeof(short), 2),
                ("Name", typeof(string), 3),
                ("Defaulted", typeof(int), 5),
                ("Big", typeof(long), (int?)200)
            ],
            Describe(contract));
    }

    [Fact]
    public void Keyed_Write_FieldCountThenKeyLengthAndPayloadInAscendingKeyOrder()
    {
        Assert.Equal<byte[]>(KeyedBytes(0x05), Write(ContractOf<ConformanceKeyed>(), Keyed()));
    }

    [Fact]
    public void Keyed_Write_NullFolded_WritesTheFieldCountOneHigher()
    {
        Assert.Equal<byte[]>(KeyedBytes(0x06), Write(ContractOf<ConformanceKeyed>(), Keyed(), nullFolded: true));
    }

    [Fact]
    public void Keyed_Read_RestoresEveryKeyedMember()
    {
        var contract = ContractOf<ConformanceKeyed>();
        AssertKeyed(Read(contract, KeyedBytes(0x05)), defaulted: 9);
        AssertKeyed(Read(contract, KeyedBytes(0x06), nullFolded: true), defaulted: 9);
    }

    [Fact]
    public void Keyed_Read_AnUnknownKey_IsDeclinedAndSkippedByItsLength()
    {
        byte[] bytes =
        [
            0x06,
            0x01, 0x04, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
            0x02, 0x02, 0x00, 0x00, 0x00, 0xFE, 0xFF,
            0x03, 0x04, 0x00, 0x00, 0x00, 0x04, 0x41, 0x64, 0x61,
            0x04, 0x03, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC,  // key 4: no member has it
            0x05, 0x04, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00,
            0xC8, 0x01, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        AssertKeyed(Read(ContractOf<ConformanceKeyed>(), bytes), defaulted: 9);
    }

    [Fact]
    public void Keyed_Read_AnAbsentKey_KeepsTheConstructorDefault()
    {
        byte[] bytes =
        [
            0x04,
            0x01, 0x04, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,
            0x02, 0x02, 0x00, 0x00, 0x00, 0xFE, 0xFF,
            0x03, 0x04, 0x00, 0x00, 0x00, 0x04, 0x41, 0x64, 0x61,
            0xC8, 0x01, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        AssertKeyed(Read(ContractOf<ConformanceKeyed>(), bytes), defaulted: 42);
    }

    // --- CONF-03: inherited ----------------------------------------------------------------------

    [Fact]
    public void Inherited_Positional_InterleavesBaseAndDerivedByNameAndKeepsTheNonPublicBaseMember()
    {
        var contract = ContractOf<ConformanceDerived>();
        var value = new ConformanceDerived { Alpha = 1, Mike = 2, Zulu = 3 };
        value.SetHidden(4);

        Assert.Equal(MemberLayout.Positional, contract.Layout);
        Assert.Equal(
            [
                ("Alpha", typeof(int), null),
                ("Mike", typeof(int), null),
                ("Zulu", typeof(int), null),
                ("_hidden", typeof(int), (int?)null)
            ],
            Describe(contract));

        byte[] bytes = Write(contract, value);
        Assert.Equal<byte[]>(
            [0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00],
            bytes);

        var read = Read(contract, bytes);
        Assert.Equal((1, 2, 3, 4), (read.Alpha, read.Mike, read.Zulu, read.GetHidden()));
    }

    [Fact]
    public void Inherited_Keyed_SharesOneKeySpaceAcrossTheHierarchy()
    {
        var contract = ContractOf<ConformanceKeyedDerived>();

        Assert.Equal(MemberLayout.Keyed, contract.Layout);
        Assert.Equal(
            [("First", typeof(int), 1), ("Second", typeof(int), 2), ("Third", typeof(int), (int?)3)],
            Describe(contract));

        byte[] bytes = Write(contract, new ConformanceKeyedDerived { First = 1, Second = 2, Third = 3 });
        Assert.Equal<byte[]>(KeyedHierarchyBytes, bytes);

        var read = Read(contract, bytes);
        Assert.Equal((1, 2, 3), (read.First, read.Second, read.Third));
    }

    [Fact]
    public void Inherited_Keyed_TheBaseReadsADerivedPayloadSkippingTheDerivedKey()
    {
        var read = Read(ContractOf<ConformanceKeyedBase>(), KeyedHierarchyBytes);

        Assert.IsType<ConformanceKeyedBase>(read);
        Assert.Equal((1, 3), (read.First, read.Third));
    }

    // --- CONF-04: shadowed -----------------------------------------------------------------------

    [Fact]
    public void Shadowed_BothDeclarationsTravel_TheBaseFirst()
    {
        var contract = ContractOf<ConformanceShadowed>();
        var value = new ConformanceShadowed { Value = "six" };
        ((ConformanceShadowBase)value).Value = 5;

        Assert.Equal(
            [("Value", typeof(int), null), ("Value", typeof(string), (int?)null)],
            Describe(contract));

        byte[] bytes = Write(contract, value);
        Assert.Equal<byte[]>([0x05, 0x00, 0x00, 0x00, 0x04, 0x73, 0x69, 0x78], bytes);

        var read = Read(contract, bytes);
        Assert.Equal("six", read.Value);
        Assert.Equal(5, ((ConformanceShadowBase)read).Value);
    }

    // --- CONF-05: overridden ---------------------------------------------------------------------

    [Fact]
    public void Overridden_IsOneMember_OrderedByTheOverridesOwnAttribute()
    {
        var contract = ContractOf<ConformanceOverridden>();

        Assert.Equal(
            [("Value", typeof(int), null), ("Other", typeof(int), (int?)null)],
            Describe(contract));

        byte[] bytes = Write(contract, new ConformanceOverridden { Value = 1, Other = 2 });
        Assert.Equal<byte[]>([0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00], bytes);

        var read = Read(contract, bytes);
        Assert.Equal((1, 2), (read.Value, read.Other));
    }

    // --- CONF-06: union --------------------------------------------------------------------------

    [Fact]
    public void Union_TheAbstractBase_DescribesItsMembersAndCannotBeConstructed()
    {
        var contract = ContractOf<ConformanceShape>();

        Assert.False(contract.CanBeConstructed);
        Assert.Equal([("Label", typeof(string), (int?)null)], Describe(contract));
    }

    [Fact]
    public void Union_EachTaggedType_WritesAndReadsItsMembersThroughThePolymorphicSlot()
    {
        var circle = ContractOf<ConformanceCircle>();
        var square = ContractOf<ConformanceSquare>();

        Assert.Equal([("Label", typeof(string), null), ("Radius", typeof(double), (int?)null)], Describe(circle));
        Assert.Equal([("Label", typeof(string), null), ("Side", typeof(int), (int?)null)], Describe(square));

        byte[] circleBytes = WriteBoxed(circle, new ConformanceCircle { Label = "c", Radius = 2.0 });
        byte[] squareBytes = WriteBoxed(square, new ConformanceSquare { Side = 3 });

        Assert.Equal<byte[]>([0x02, 0x63, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40], circleBytes);
        Assert.Equal<byte[]>([0x00, 0x03, 0x00, 0x00, 0x00], squareBytes);

        var readCircle = Assert.IsType<ConformanceCircle>(ReadBoxed(circle, circleBytes));
        var readSquare = Assert.IsType<ConformanceSquare>(ReadBoxed(square, squareBytes));
        Assert.Equal(("c", 2.0), (readCircle.Label, readCircle.Radius));
        Assert.Equal(((string?)null, 3), (readSquare.Label, readSquare.Side));
    }

    // --- CONF-07: struct -------------------------------------------------------------------------

    [Fact]
    public void Struct_Positional_IsCreatedAsDefaultAndReadInPlace()
    {
        var contract = ContractOf<ConformancePoint>();

        Assert.Equal(MemberLayout.Positional, contract.Layout);
        Assert.True(contract.CanBeConstructed);
        Assert.Equal(default, contract.Create());
        Assert.Equal([("X", typeof(int), null), ("Y", typeof(int), (int?)null)], Describe(contract));

        byte[] bytes = Write(contract, new ConformancePoint { X = 1, Y = -1 });
        Assert.Equal<byte[]>([0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF], bytes);
        Assert.Equal(new ConformancePoint { X = 1, Y = -1 }, Read(contract, bytes));
    }

    [Fact]
    public void Struct_Keyed_WritesTheFieldCountAsItIs()
    {
        var contract = ContractOf<ConformanceKeyedPoint>();

        Assert.Equal(MemberLayout.Keyed, contract.Layout);
        Assert.Equal([("X", typeof(int), 1), ("Tag", typeof(string), (int?)2)], Describe(contract));

        byte[] bytes = Write(contract, new ConformanceKeyedPoint { X = 5, Tag = "t" });
        Assert.Equal<byte[]>(
            [0x02, 0x01, 0x04, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x02, 0x02, 0x00, 0x00, 0x00, 0x02, 0x74],
            bytes);
        Assert.Equal(new ConformanceKeyedPoint { X = 5, Tag = "t" }, Read(contract, bytes));
    }

    [Fact]
    public void Struct_InThePolymorphicSlot_IsReadIntoItsBox()
    {
        var contract = ContractOf<ConformancePoint>();
        byte[] bytes = WriteBoxed(contract, new ConformancePoint { X = 1, Y = -1 });

        Assert.Equal<byte[]>([0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF], bytes);
        Assert.Equal(new ConformancePoint { X = 1, Y = -1 }, Assert.IsType<ConformancePoint>(ReadBoxed(contract, bytes)));
    }

    // --- the corpus ------------------------------------------------------------------------------

    private static ConformancePositional Positional()
    {
        var value = new ConformancePositional
        {
            Id = 7,
            Flag = true,
            Name = "Ada",
            Origin = new ConformancePoint { X = 1, Y = -1 },
            Skipped = 99
        };
        value.SetRatio(0.5);
        return value;
    }

    private static ConformanceKeyed Keyed()
    {
        var value = new ConformanceKeyed { Id = 7, Name = "Ada", Defaulted = 9, Big = 1, Skipped = 99 };
        value.SetCode(-2);
        return value;
    }

    private static void AssertKeyed(ConformanceKeyed read, int defaulted)
    {
        Assert.Equal(7, read.Id);
        Assert.Equal(-2, read.GetCode());
        Assert.Equal("Ada", read.Name);
        Assert.Equal(defaulted, read.Defaulted);
        Assert.Equal(1L, read.Big);
        Assert.Equal(0, read.Skipped);
    }

    /// <summary><see cref="Keyed"/> with its field count written as <paramref name="count"/>.</summary>
    private static byte[] KeyedBytes(byte count) =>
    [
        count,
        0x01, 0x04, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00,              // key 1 Id
        0x02, 0x02, 0x00, 0x00, 0x00, 0xFE, 0xFF,                          // key 2 _code
        0x03, 0x04, 0x00, 0x00, 0x00, 0x04, 0x41, 0x64, 0x61,              // key 3 Name "Ada"
        0x05, 0x04, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00,              // key 5 Defaulted
        0xC8, 0x01, 0x08, 0x00, 0x00, 0x00,                                // key 200, two varint bytes
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00                     // Big
    ];

    private static readonly byte[] KeyedHierarchyBytes =
    [
        0x03,
        0x01, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x02, 0x04, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
        0x03, 0x04, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00
    ];

    // --- the engine's entries for a contract -----------------------------------------------------

    private static (string Name, Type Type, int? Key)[] Describe<T>(TypeContract<T> contract) =>
        [.. contract.Members.Select(member => (member.Name, member.MemberType, member.Key))];

    /// <summary>The members of <paramref name="value"/>, written through the engine's entry for a declared type's contract.</summary>
    private static byte[] Write<T>(TypeContract<T> contract, T value, bool nullFolded = false) =>
        Capture((ref WireWriter writer) => ObjectMembers.Write(ref writer, contract, in value, nullFolded));

    /// <summary>The members of <paramref name="value"/>, written through the polymorphic slot's entry.</summary>
    private static byte[] WriteBoxed(ITypeContract contract, object value) =>
        Capture((ref WireWriter writer) =>
        {
            var members = MemberWriter.Begin(ref writer, contract, nullFolded: false);
            contract.WriteBoxed(ref members, value);
            members.End(ref writer);
        });

    /// <summary>A new instance read through the engine's entry for a declared type's contract, which must consume every byte.</summary>
    private static T Read<T>(TypeContract<T> contract, byte[] bytes, bool nullFolded = false)
    {
        var operation = new OperationBox();
        var reader = new WireReader(bytes, ref operation.State);
        var value = ObjectMembers.ReadInstance(ref reader, contract, referenceId: -1, target: null, nullFolded);
        Assert.Equal(0, reader.Remaining);
        return value;
    }

    /// <summary>A new instance read through the polymorphic slot's entry, which must consume every byte.</summary>
    private static object ReadBoxed(ITypeContract contract, byte[] bytes)
    {
        var operation = new OperationBox();
        var reader = new WireReader(bytes, ref operation.State);
        var value = contract.ReadBoxed(ref reader, referenceId: -1, target: null);
        Assert.Equal(0, reader.Remaining);
        return value;
    }

    private delegate void WriteAction(ref WireWriter writer);

    private static byte[] Capture(WriteAction write)
    {
        var operation = new OperationBox();
        using var buffer = new PayloadBuffer(4096, "payload");
        var writer = new WireWriter(buffer, ref operation.State);
        write(ref writer);
        writer.Flush();

        byte[] bytes = new byte[buffer.Length];
        buffer.CopyTo(bytes);
        return bytes;
    }
}

/// <summary>The conformance suite run against the contract built by reflection, the one every other contract must match.</summary>
public sealed class ReflectedContractConformanceTests : ContractConformance
{
    internal override TypeContract<T> ContractOf<T>() => TypeContractCache.Get<T>();
}

/// <summary>
/// Pins GEN-11: the conformance suite run a second time, against the contracts the source generator
/// writes for the same shapes (<c>Fixtures/GeneratedContracts</c>), reached through the options their
/// context was given to.
/// </summary>
public sealed class GeneratedContractConformanceTests : ContractConformance
{
    private static readonly BinarySerializerOptions Options =
        BinarySerializerOptions.Configure().WithContracts(GeneratedConformanceContracts.Default).Build();

    internal override TypeContract<T> ContractOf<T>() =>
        Options.ContractSet!.Find<T>() ?? throw new InvalidOperationException($"No generated contract for '{typeof(T)}'.");
}
