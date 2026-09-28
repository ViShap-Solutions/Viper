using ViShap.Viper.Engine;
using ViShap.Viper.Io;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-31 and CTR-32: a type contract supplies member order, access, construction and the
/// response to a known key, and the engine checks every call it makes against its own description.
/// A contract that writes or reads a member of the wrong type, under the wrong key, out of order, too
/// few or too many times — or that accepts a keyed field without reading it, or reads one and then
/// disowns it — is <see cref="BinaryTypeException"/> naming the type and the member, never a
/// distorted wire. The contracts below are scripted doubles: each does exactly one thing wrong.
/// </summary>
public class ContractCallTests
{
    private static readonly MemberDescription[] Positional =
    [
        new("A", typeof(int), key: null),
        new("B", typeof(string), key: null)
    ];

    private static readonly MemberDescription[] Keyed =
    [
        new("A", typeof(int), key: 1),
        new("B", typeof(string), key: 2)
    ];

    // --- CTR-31: writing ---------------------------------------------------------------------------

    [Fact]
    public void Write_ACallPerMemberInPlanOrder_WritesWhatTheReflectedContractWrites()
    {
        var scripted = Contract(MemberLayout.Positional, Positional, write: (ref MemberWriter w, Pair v) =>
        {
            w.Member(v.A);
            w.Member(v.B);
        });

        var value = new Pair { A = 7, B = "seven" };

        Assert.Equal(WriteWith(TypeContractCache.Get<Pair>(), value), WriteWith(scripted, value));
    }

    [Fact]
    public void Write_AMemberOfTheWrongType_ThrowsTypeNamingTheMember()
    {
        var contract = Contract(MemberLayout.Positional, Positional, write: (ref MemberWriter w, Pair v) =>
        {
            w.Member((long)v.A);
            w.Member(v.B);
        });

        var ex = AssertEx.Throws<BinaryTypeException>("'A'", () => WriteWith(contract, new Pair()));
        Assert.Contains(nameof(Pair), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_MembersOutOfOrder_ThrowsType()
    {
        var contract = Contract(MemberLayout.Positional, Positional, write: (ref MemberWriter w, Pair v) =>
        {
            w.Member(v.B);
            w.Member(v.A);
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => WriteWith(contract, new Pair { B = "b" }));
    }

    [Fact]
    public void Write_TooFewMembers_ThrowsTypeNamingTheFirstMissing()
    {
        var contract = Contract(MemberLayout.Positional, Positional, write: (ref MemberWriter w, Pair v) =>
            w.Member(v.A));

        AssertEx.Throws<BinaryTypeException>("'B'", () => WriteWith(contract, new Pair()));
    }

    [Fact]
    public void Write_TooManyMembers_ThrowsType()
    {
        var contract = Contract(MemberLayout.Positional, Positional, write: (ref MemberWriter w, Pair v) =>
        {
            w.Member(v.A);
            w.Member(v.B);
            w.Member(v.A);
        });

        var ex = Assert.Throws<BinaryTypeException>(() => WriteWith(contract, new Pair()));
        Assert.Contains(nameof(Pair), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_AKeyedFieldUnderAPositionalLayout_ThrowsType()
    {
        var contract = Contract(MemberLayout.Positional, Positional, write: (ref MemberWriter w, Pair v) =>
        {
            w.Field(1, v.A);
            w.Field(2, v.B);
        });

        Assert.Throws<BinaryTypeException>(() => WriteWith(contract, new Pair()));
    }

    [Fact]
    public void Write_APositionalMemberUnderAKeyedLayout_ThrowsType()
    {
        var contract = Contract(MemberLayout.Keyed, Keyed, write: (ref MemberWriter w, Pair v) =>
        {
            w.Member(v.A);
            w.Member(v.B);
        });

        Assert.Throws<BinaryTypeException>(() => WriteWith(contract, new Pair()));
    }

    [Fact]
    public void Write_TheWrongKey_ThrowsTypeNamingTheMember()
    {
        var contract = Contract(MemberLayout.Keyed, Keyed, write: (ref MemberWriter w, Pair v) =>
        {
            w.Field(3, v.A);
            w.Field(2, v.B);
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => WriteWith(contract, new Pair()));
    }

    [Fact]
    public void Write_KeysOutOfOrder_ThrowsType()
    {
        var contract = Contract(MemberLayout.Keyed, Keyed, write: (ref MemberWriter w, Pair v) =>
        {
            w.Field(2, v.B);
            w.Field(1, v.A);
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => WriteWith(contract, new Pair()));
    }

    [Fact]
    public void Write_AKeyedFieldOfTheWrongType_ThrowsType()
    {
        var contract = Contract(MemberLayout.Keyed, Keyed, write: (ref MemberWriter w, Pair v) =>
        {
            w.Field(1, (short)v.A);
            w.Field(2, v.B);
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => WriteWith(contract, new Pair()));
    }

    [Fact]
    public void Write_TooFewKeyedFields_ThrowsType()
    {
        var contract = Contract(MemberLayout.Keyed, Keyed, write: (ref MemberWriter w, Pair v) =>
            w.Field(1, v.A));

        AssertEx.Throws<BinaryTypeException>("'B'", () => WriteWith(contract, new Pair()));
    }

    // --- CTR-31: reading positionally --------------------------------------------------------------

    [Fact]
    public void Read_AMemberOfTheWrongType_ThrowsTypeNamingTheMember()
    {
        byte[] bytes = WriteWith(TypeContractCache.Get<Pair>(), new Pair { A = 1, B = "b" });
        var contract = Contract(MemberLayout.Positional, Positional, read: (ref MemberReader r, ref Pair v) =>
        {
            v.A = (int)r.Member<long>();
            v.B = r.Member<string>();
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void Read_TooFewMembers_ThrowsType()
    {
        byte[] bytes = WriteWith(TypeContractCache.Get<Pair>(), new Pair { A = 1, B = "b" });
        var contract = Contract(MemberLayout.Positional, Positional, read: (ref MemberReader r, ref Pair v) =>
            v.A = r.Member<int>());

        AssertEx.Throws<BinaryTypeException>("'B'", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void Read_TooManyMembers_ThrowsType()
    {
        byte[] bytes = WriteWith(TypeContractCache.Get<Pair>(), new Pair { A = 1, B = "b" });
        var contract = Contract(MemberLayout.Positional, Positional, read: (ref MemberReader r, ref Pair v) =>
        {
            v.A = r.Member<int>();
            v.B = r.Member<string>();
            v.A = r.Member<int>();
        });

        Assert.Throws<BinaryTypeException>(() => ReadWith(contract, bytes));
    }

    // --- CTR-31: reading keyed fields ------------------------------------------------------------

    [Fact]
    public void ReadField_AcceptingAKeyWithoutReadingIt_ThrowsType()
    {
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, Keyed, field: (ref MemberReader r, int key, ref Pair v) => true);

        AssertEx.Throws<BinaryTypeException>("key 1", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void ReadField_ReadingAKeyAndThenDisowningIt_ThrowsType()
    {
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, Keyed, field: (ref MemberReader r, int key, ref Pair v) =>
        {
            v.A = r.Value<int>();
            return false;
        });

        AssertEx.Throws<BinaryTypeException>("key 1", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void ReadField_ReadingAValueTwice_ThrowsType()
    {
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, Keyed, field: (ref MemberReader r, int key, ref Pair v) =>
        {
            v.A = r.Value<int>();
            v.A = r.Value<int>();
            return true;
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void ReadField_AValueOfTheWrongType_ThrowsTypeNamingTheMember()
    {
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, Keyed, field: (ref MemberReader r, int key, ref Pair v) =>
        {
            _ = r.Value<string>();
            return true;
        });

        AssertEx.Throws<BinaryTypeException>("'A'", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void ReadField_ReadingAKeyTheDescriptionDoesNotHave_ThrowsType()
    {
        MemberDescription[] onlyA = [new("A", typeof(int), key: 1)];
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, onlyA, field: (ref MemberReader r, int key, ref Pair v) =>
        {
            v.A = r.Value<int>();
            return true;
        });

        AssertEx.Throws<BinaryTypeException>("key 2", () => ReadWith(contract, bytes));
    }

    [Fact]
    public void ReadField_DecliningAnUnknownKey_SkipsItsField()
    {
        MemberDescription[] onlyA = [new("A", typeof(int), key: 1)];
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, onlyA, field: (ref MemberReader r, int key, ref Pair v) =>
        {
            if (key != 1)
                return false;

            v.A = r.Value<int>();
            return true;
        });

        var pair = ReadWith(contract, bytes);

        Assert.Equal(11, pair.A);
        Assert.Null(pair.B);
    }

    [Fact]
    public void ReadField_APositionalMemberUnderAKeyedLayout_ThrowsType()
    {
        byte[] bytes = KeyedBytes();
        var contract = Contract(MemberLayout.Keyed, Keyed, field: (ref MemberReader r, int key, ref Pair v) =>
        {
            v.A = r.Member<int>();
            return true;
        });

        Assert.Throws<BinaryTypeException>(() => ReadWith(contract, bytes));
    }

    // --- CTR-32: a struct owner is assigned in place ---------------------------------------------

    [Fact]
    public void Read_AStructOwner_IsPopulatedInPlaceThroughTheRefSetter()
    {
        var contract = TypeContractCache.Get<PointStruct>();
        byte[] bytes = WriteWith(contract, new PointStruct { X = 3, Y = 4 });

        var operation = new OperationBox();
        var reader = new WireReader(bytes, ref operation.State);
        var members = MemberReader.Positional(ref reader, contract);

        var target = new PointStruct();
        contract.Read(ref members, ref target);
        members.End(ref reader);

        Assert.Equal(3, target.X);
        Assert.Equal(4, target.Y);
    }

    [Fact]
    public void Deserialize_AStructOwner_IsReadIntoTheInstanceItReturns()
    {
        var serializer = new BinarySerializer();

        var point = serializer.Deserialize<PointStruct>(serializer.Serialize(new PointStruct { X = 5, Y = -6 }));

        Assert.Equal(5, point.X);
        Assert.Equal(-6, point.Y);
    }

    // --- the double and the engine entry --------------------------------------------------------

    /// <summary>The target the scripted contracts describe.</summary>
    public sealed class Pair
    {
        public int A { get; set; }

        public string? B { get; set; }
    }

    private delegate void WriteScript(ref MemberWriter writer, Pair value);

    private delegate void ReadScript(ref MemberReader reader, ref Pair value);

    private delegate bool FieldScript(ref MemberReader reader, int key, ref Pair value);

    /// <summary>A contract for <see cref="Pair"/> whose calls are whatever the test scripts.</summary>
    private sealed class ScriptedContract(
        MemberLayout layout,
        MemberDescription[] members,
        WriteScript? write,
        ReadScript? read,
        FieldScript? field) : TypeContract<Pair>(layout, members, canBeConstructed: true)
    {
        public override Pair Create() => new();

        public override void Write(ref MemberWriter writer, in Pair value) => write!(ref writer, value);

        public override void Read(ref MemberReader reader, ref Pair value) => read!(ref reader, ref value);

        public override bool ReadField(ref MemberReader reader, int key, ref Pair value) =>
            field!(ref reader, key, ref value);
    }

    private static ScriptedContract Contract(
        MemberLayout layout,
        MemberDescription[] members,
        WriteScript? write = null,
        ReadScript? read = null,
        FieldScript? field = null) =>
        new(layout, members, write, read, field);

    /// <summary>A keyed Pair { A = 11, B = "eleven" } as a correct keyed contract writes it.</summary>
    private static byte[] KeyedBytes() =>
        WriteWith(
            Contract(MemberLayout.Keyed, Keyed, write: (ref MemberWriter w, Pair v) =>
            {
                w.Field(1, v.A);
                w.Field(2, v.B);
            }),
            new Pair { A = 11, B = "eleven" });

    /// <summary>The members of <paramref name="value"/>, written through the engine's entry for a contract.</summary>
    private static byte[] WriteWith<T>(TypeContract<T> contract, T value)
    {
        var operation = new OperationBox();
        using var buffer = new PayloadBuffer(4096, "payload");
        var writer = new WireWriter(buffer, ref operation.State);
        ObjectMembers.Write(ref writer, contract, in value, nullFolded: false);
        writer.Flush();

        byte[] bytes = new byte[buffer.Length];
        buffer.CopyTo(bytes);
        return bytes;
    }

    /// <summary>A new instance read through the engine's entry for a contract, which must consume every byte.</summary>
    private static T ReadWith<T>(TypeContract<T> contract, byte[] bytes)
    {
        var operation = new OperationBox();
        var reader = new WireReader(bytes, ref operation.State);
        var value = ObjectMembers.ReadInstance(ref reader, contract, referenceId: -1, target: null, nullFolded: false);
        Assert.Equal(0, reader.Remaining);
        return value;
    }
}
