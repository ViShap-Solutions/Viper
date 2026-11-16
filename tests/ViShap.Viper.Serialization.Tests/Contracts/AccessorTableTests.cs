using ViShap.Viper.Contracts;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-33…CTR-38: a contract hands the serializer an accessor table once, from its constructor,
/// and the table is held to the contract's description — one entry per member, each for the member's
/// type — before anything is written with it; a contract whose table is held to its description writes
/// and reads what the reflection path writes and reads.
/// </summary>
public class AccessorTableTests
{
    public sealed class Point
    {
        public int X { get; set; }

        public string? Label { get; set; }
    }

    private sealed class PointContract : TypeContract<Point>
    {
        public PointContract()
            : base(
                MemberLayout.Positional,
                [new MemberDescription("Label", typeof(string), null), new MemberDescription("X", typeof(int), null)],
                canBeConstructed: true)
        {
        }

        public static MemberAccessors<Point> Table() =>
            new MemberAccessors<Point>()
                .Member(static (in Point o) => o.Label, static (ref Point o, string? v) => o.Label = v)
                .Member(static (in Point o) => o.X, static (ref Point o, int v) => o.X = v);

        public void HandOver(MemberAccessors<Point> table) => UseAccessors(table);

        public override Point Create() => new();

        public override void Write(ref MemberWriter writer, in Point value)
        {
            writer.Member(value.Label);
            writer.Member(value.X);
        }

        public override void ReadPositional(ref MemberReader reader, ref Point value)
        {
            value.Label = reader.Member<string?>();
            value.X = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Point value) => false;
    }

    private sealed class PointContext : BinarySerializerContext
    {
        public PointContext(PointContract contract) => Add(contract);
    }

    [Fact]
    public void UseAccessors_ATableShorterThanTheDescription_ThrowsConfiguration()
    {
        var contract = new PointContract();
        var table = new MemberAccessors<Point>().Member(static (in Point o) => o.Label, static (ref Point o, string? v) => o.Label = v);

        AssertEx.Throws<BinaryConfigurationException>("describes 2 members", () => contract.HandOver(table));
    }

    [Fact]
    public void UseAccessors_AnEntryForAnotherTypeThanItsMember_ThrowsConfiguration()
    {
        var contract = new PointContract();
        var table = new MemberAccessors<Point>()
            .Member(static (in Point o) => o.Label, static (ref Point o, string? v) => o.Label = v)
            .Member(static (in Point o) => (long)o.X, static (ref Point o, long v) => o.X = (int)v);

        AssertEx.Throws<BinaryConfigurationException>("'X'", () => contract.HandOver(table));
    }

    [Fact]
    public void UseAccessors_Twice_ThrowsConfiguration()
    {
        var contract = new PointContract();
        contract.HandOver(PointContract.Table());

        AssertEx.Throws<BinaryConfigurationException>("already handed over", () => contract.HandOver(PointContract.Table()));
    }

    [Fact]
    public void UseAccessors_AfterTheContractWasAddedToAContext_ThrowsConfiguration()
    {
        var contract = new PointContract();
        _ = new PointContext(contract);

        AssertEx.Throws<BinaryConfigurationException>("already in use", () => contract.HandOver(PointContract.Table()));
    }

    [Fact]
    public void UseAccessors_ANullTableOrAccessor_ThrowsArgumentNull()
    {
        var contract = new PointContract();

        Assert.Throws<ArgumentNullException>(() => contract.HandOver(null!));
        Assert.Throws<ArgumentNullException>(() => new MemberAccessors<Point>().Member<int>(null!, static (ref Point o, int v) => o.X = v));
        Assert.Throws<ArgumentNullException>(() => new MemberAccessors<Point>().Member(static (in Point o) => o.X, null!));
    }

    [Fact]
    public void Serialize_AContractWithATable_WritesAndReadsWhatTheReflectionPathDoes()
    {
        var contract = new PointContract();
        contract.HandOver(PointContract.Table());
        var withTable = new BinarySerializer(BinarySerializerOptions.Configure().WithContracts(new PointContext(contract)).Build());
        var value = new Point { X = 7, Label = "seven" };

        byte[] written = withTable.Serialize(value);
        var read = withTable.Deserialize<Point>(written)!;

        Assert.Equal<byte[]>(new BinarySerializer().Serialize(value), written);
        Assert.Equal(7, read.X);
        Assert.Equal("seven", read.Label);
    }
}
