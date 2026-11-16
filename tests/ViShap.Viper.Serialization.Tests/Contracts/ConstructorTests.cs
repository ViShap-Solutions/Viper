using ViShap.Viper.Contracts;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTR-39…CTR-56: a class with no parameterless constructor is read through a constructor with
/// parameters — the one marked <see cref="BinaryConstructorAttribute"/>, else its single public one whose
/// parameters name its members — each parameter receiving the member of its name, the other members
/// assigned after; a keyed field the payload leaves out gives a parameter its default; a cycle through
/// such a type is refused on write and read, and it cannot be populated. A contract hands the same
/// construction over through <c>UseConstructor</c>, held to its description.
/// </summary>
public class ConstructorTests
{
    public sealed record Money(decimal Amount, string Currency);

    public sealed class MutableMoney
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = "";
    }

    public sealed class Order
    {
        public Order(int id, string? customer)
        {
            Id = id;
            Customer = customer;
        }

        public int Id { get; init; }

        public string? Customer { get; init; }

        public string? Note { get; set; }
    }

    public sealed class Sealed
    {
        public Sealed() => Value = -1;

        [BinaryConstructor]
        public Sealed(int value, string name)
        {
            Value = value;
            Name = name;
        }

        public int Value { get; }

        public string Name { get; } = "";
    }

    public sealed class SealedShape
    {
        public string Name { get; set; } = "";

        public int Value { get; set; }
    }

    public sealed class TwoMarked
    {
        [BinaryConstructor]
        public TwoMarked(int value) => Value = value;

        [BinaryConstructor]
        public TwoMarked(long value) => Value = (int)value;

        public int Value { get; set; }
    }

    public sealed class Mismatched
    {
        [BinaryConstructor]
        public Mismatched(int count) => Value = count;

        public int Value { get; set; }
    }

    public sealed class WrongType
    {
        public WrongType(long value) => Value = (int)value;

        public int Value { get; set; }
    }

    [BinaryContract]
    public sealed class SettingsV1
    {
        [BinaryKey(0)]
        public string Name { get; set; } = "";
    }

    [BinaryContract]
    public sealed class Settings
    {
        public Settings(string name, int retries = 3, DayOfWeek day = DayOfWeek.Friday, string? region = null)
        {
            Name = name;
            Retries = retries;
            Day = day;
            Region = region;
        }

        [BinaryKey(0)]
        public string Name { get; init; }

        [BinaryKey(1)]
        public int Retries { get; init; }

        [BinaryKey(2)]
        public DayOfWeek Day { get; init; }

        [BinaryKey(3)]
        public string? Region { get; init; }

        [BinaryKey(4)]
        public string Label { get; set; } = "initial";
    }

    public sealed class Node
    {
        public Node(string name, Node? next)
        {
            Name = name;
            Next = next;
        }

        public string Name { get; init; }

        public Node? Next { get; set; }
    }

    public sealed class MutableNode
    {
        public string Name { get; set; } = "";

        public MutableNode? Next { get; set; }
    }

    public sealed class Pair
    {
        public Node? Left { get; set; }

        public Node? Right { get; set; }
    }

    public readonly struct Amount
    {
        [BinaryConstructor]
        public Amount(decimal value, string currency)
        {
            Value = value;
            Currency = currency;
        }

        public decimal Value { get; }

        public string Currency { get; }
    }

    public struct AmountShape
    {
        public string Currency { get; set; }

        public decimal Value { get; set; }
    }

    [BinaryContract]
    public struct RetryPolicy
    {
        [BinaryConstructor]
        public RetryPolicy() => Retries = 3;

        [BinaryKey(0)]
        public string? Name { get; set; }

        [BinaryKey(1)]
        public int Retries { get; set; }
    }

    [BinaryContract]
    public struct NameOnly
    {
        [BinaryKey(0)]
        public string? Name { get; set; }
    }

    [BinaryContract]
    public readonly struct Window
    {
        [BinaryConstructor]
        public Window(int start, int length = 10)
        {
            Start = start;
            Length = length;
        }

        [BinaryKey(0)]
        public int Start { get; }

        [BinaryKey(1)]
        public int Length { get; }
    }

    [BinaryContract]
    public struct StartOnly
    {
        [BinaryKey(0)]
        public int Start { get; set; }
    }

    private static readonly BinarySerializer Serializer = new();

    private static readonly BinarySerializer Framed =
        new(new BinarySerializerOptionsBuilder().PreserveReferences().Build());

    [Fact]
    public void Deserialize_RecordWithAPrimaryConstructor_ReadsThroughIt()
    {
        var payload = Serializer.Serialize(new Money(12.5m, "EUR"));

        var restored = Serializer.Deserialize<Money>(payload);

        Assert.Equal(new Money(12.5m, "EUR"), restored);
    }

    [Fact]
    public void Serialize_RecordWithAPrimaryConstructor_WritesTheBytesOfTheSameMembersAssigned()
    {
        var constructed = Serializer.Serialize(new Money(12.5m, "EUR"));

        var assigned = Serializer.Serialize(new MutableMoney { Amount = 12.5m, Currency = "EUR" });

        Assert.Equal(assigned, constructed);
    }

    [Fact]
    public void Deserialize_AMemberTheConstructorDoesNotTake_IsAssignedAfter()
    {
        var payload = Serializer.Serialize(new Order(7, "Ann") { Note = "rush" });

        var restored = Serializer.Deserialize<Order>(payload)!;

        Assert.Equal((7, "Ann", "rush"), (restored.Id, restored.Customer, restored.Note));
    }

    [Fact]
    public void Deserialize_AMarkedConstructor_WinsOverTheParameterlessOne()
    {
        var payload = Serializer.Serialize(new Sealed(42, "x"));

        var restored = Serializer.Deserialize<Sealed>(payload)!;

        Assert.Equal((42, "x"), (restored.Value, restored.Name));
    }

    [Fact]
    public void Serialize_AMarkedConstructor_WritesTheMembersOnlyItSets()
    {
        var constructed = Serializer.Serialize(new Sealed(42, "x"));

        var assigned = Serializer.Serialize(new SealedShape { Name = "x", Value = 42 });

        Assert.Equal(assigned, constructed);
    }

    [Fact]
    public void Deserialize_AStructWithAMarkedConstructor_ReadsThroughIt()
    {
        var payload = Serializer.Serialize(new Amount(4.5m, "USD"));

        var restored = Serializer.Deserialize<Amount>(payload);

        Assert.Equal((4.5m, "USD"), (restored.Value, restored.Currency));
    }

    [Fact]
    public void Serialize_AStructWithAMarkedConstructor_WritesTheBytesOfTheSameMembersAssigned()
    {
        var constructed = Serializer.Serialize(new Amount(4.5m, "USD"));

        var assigned = Serializer.Serialize(new AmountShape { Currency = "USD", Value = 4.5m });

        Assert.Equal(assigned, constructed);
    }

    [Fact]
    public void Deserialize_AStructWithAMarkedParameterlessConstructor_RunsItBeforeTheMembers()
    {
        var payload = Serializer.Serialize(new NameOnly { Name = "fast" });

        var restored = Serializer.Deserialize<RetryPolicy>(payload);

        Assert.Equal(("fast", 3), (restored.Name, restored.Retries));
    }

    [Fact]
    public void Deserialize_AKeyedStructFieldThePayloadLeavesOut_TakesTheParametersDefault()
    {
        var payload = Serializer.Serialize(new StartOnly { Start = 5 });

        var restored = Serializer.Deserialize<Window>(payload);

        Assert.Equal((5, 10), (restored.Start, restored.Length));
    }

    [BinaryUnion(1, typeof(Tagged))]
    public interface IValue;

    public readonly struct Tagged : IValue
    {
        [BinaryConstructor]
        public Tagged(string tag) => Tag = tag;

        public string Tag { get; }
    }

    public sealed class Holder
    {
        public IValue? Value { get; set; }
    }

    [Fact]
    public void Deserialize_AStructWithAMarkedConstructorInAUnionSlot_ReadsThroughIt()
    {
        var payload = Serializer.Serialize(new Holder { Value = new Tagged("t") });

        var restored = Serializer.Deserialize<Holder>(payload)!;

        Assert.Equal("t", Assert.IsType<Tagged>(restored.Value).Tag);
    }

    [Fact]
    public void Serialize_TwoMarkedConstructors_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>("[BinaryConstructor]", () => Serializer.Serialize(new TwoMarked(1)));
    }

    [Fact]
    public void Serialize_AMarkedConstructorWithAParameterNamingNoMember_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>("'count'", () => Serializer.Serialize(new Mismatched(1)));
    }

    [Fact]
    public void Deserialize_TheSingleConstructorWithAParameterOfAnotherTypeThanItsMember_ThrowsType()
    {
        var payload = Serializer.Serialize(new WrongType(5));

        AssertEx.Throws<BinaryTypeException>("cannot be constructed", () => Serializer.Deserialize<WrongType>(payload));
    }

    [Fact]
    public void Deserialize_KeyedFieldsThePayloadLeavesOut_TakeTheParametersDefaults()
    {
        var payload = Serializer.Serialize(new SettingsV1 { Name = "main" });

        var restored = Serializer.Deserialize<Settings>(payload)!;

        Assert.Equal(("main", 3, DayOfWeek.Friday, (string?)null, "initial"),
            (restored.Name, restored.Retries, restored.Day, restored.Region, restored.Label));
    }

    [Fact]
    public void Deserialize_KeyedFieldsThePayloadCarries_ReplaceTheDefaults()
    {
        var original = new Settings("main", 9, DayOfWeek.Monday, "eu") { Label = "set" };

        var restored = Serializer.Deserialize<Settings>(Serializer.Serialize(original))!;

        Assert.Equal(("main", 9, DayOfWeek.Monday, "eu", "set"),
            (restored.Name, restored.Retries, restored.Day, restored.Region, restored.Label));
    }

    [Fact]
    public void Serialize_ACycleThroughAConstructedType_ThrowsType()
    {
        var head = new Node("a", next: null);
        head.Next = new Node("b", head);

        AssertEx.Throws<BinaryTypeException>("constructor", () => Framed.Serialize(head));
    }

    [Fact]
    public void Deserialize_ABackReferenceToAConstructedAncestor_ThrowsFormat()
    {
        var head = new MutableNode { Name = "a" };
        head.Next = new MutableNode { Name = "b", Next = head };
        var payload = Framed.Serialize(head);

        AssertEx.Throws<BinaryFormatException>("still being constructed", () => Framed.Deserialize<Node>(payload));
    }

    [Fact]
    public void Deserialize_AConstructedValueSharedBySiblings_KeepsOneInstance()
    {
        var shared = new Node("s", next: null);
        var payload = Framed.Serialize(new Pair { Left = shared, Right = shared });

        var restored = Framed.Deserialize<Pair>(payload)!;

        Assert.Same(restored.Left, restored.Right);
    }

    [Fact]
    public void Populate_AConstructedType_ThrowsType()
    {
        var payload = Serializer.Serialize(new Order(1, "x"));

        AssertEx.Throws<BinaryTypeException>("existing instance", () => Serializer.Populate(payload, new Order(0, null)));
    }

    [Fact]
    public void DumpOf_AConstructedType_ShowsEveryMember()
    {
        var payload = Serializer.Serialize(new Order(7, "Ann") { Note = "rush" });

        var dump = BinaryFormatDumper.Dump<Order>(payload);

        Assert.Null(dump.Failure);
        Assert.Equal(["Customer", "Id", "Note"], dump.Root!.Children.Select(child => child.Name));
    }

    public sealed class Point
    {
        [BinaryConstructor]
        public Point(int x, string? label)
        {
            X = x;
            Label = label;
        }

        public int X { get; }

        public string? Label { get; }
    }

    private struct PointValues
    {
        public PointValues()
        {
        }

        public string? Label = "none";
        public int X;
    }

    private sealed class PointContract : TypeContract<Point>
    {
        public PointContract(bool handOver = true)
            : base(
                MemberLayout.Positional,
                [new MemberDescription("Label", typeof(string), null), new MemberDescription("X", typeof(int), null)],
                canBeConstructed: false)
        {
            if (!handOver)
                return;

            UseConstructor(static () => new PointValues(), Values(), static (ref PointValues v) => new Point(v.X, v.Label));
            UseAccessors(new MemberAccessors<Point>()
                .Member(static (in Point o) => o.Label)
                .Member(static (in Point o) => o.X));
        }

        public static MemberAccessors<PointValues> Values() =>
            new MemberAccessors<PointValues>()
                .Member(static (in PointValues v) => v.Label, static (ref PointValues v, string? m) => v.Label = m)
                .Member(static (in PointValues v) => v.X, static (ref PointValues v, int m) => v.X = m);

        public void HandOver(MemberAccessors<PointValues> values) =>
            UseConstructor(static () => new PointValues(), values, static (ref PointValues v) => new Point(v.X, v.Label));

        public void HandOverReadOnly(MemberAccessors<Point> table) => UseAccessors(table);

        public override Point Create() => throw new InvalidOperationException();

        public override void Write(ref MemberWriter writer, in Point value)
        {
            writer.Member(value.Label);
            writer.Member(value.X);
        }

        public override void ReadPositional(ref MemberReader reader, ref Point value) => throw new InvalidOperationException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Point value) => false;
    }

    private sealed class PointContext : BinarySerializerContext
    {
        public PointContext() => Add(new PointContract());
    }

    [Fact]
    public void UseConstructor_AContractReadingThroughItsValues_ReadsWhatTheReflectionPathWrites()
    {
        var options = new BinarySerializerOptionsBuilder().WithContracts(new PointContext()).Build();
        var payload = Serializer.Serialize(new Point(3, "p"));

        var restored = new BinarySerializer(options).Deserialize<Point>(payload)!;

        Assert.Equal((3, "p"), (restored.X, restored.Label));
    }

    [Fact]
    public void UseConstructor_AContractWritingThroughGetters_WritesTheReflectionPathsBytes()
    {
        var options = new BinarySerializerOptionsBuilder().WithContracts(new PointContext()).Build();

        var written = new BinarySerializer(options).Serialize(new Point(3, "p"));

        Assert.Equal(Serializer.Serialize(new Point(3, "p")), written);
    }

    [Fact]
    public void UseConstructor_AValuesTableShorterThanTheDescription_ThrowsConfiguration()
    {
        var contract = new PointContract(handOver: false);
        var values = new MemberAccessors<PointValues>()
            .Member(static (in PointValues v) => v.Label, static (ref PointValues v, string? m) => v.Label = m);

        AssertEx.Throws<BinaryConfigurationException>("describes 2 members", () => contract.HandOver(values));
    }

    [Fact]
    public void UseConstructor_AValuesEntryWithoutASetter_ThrowsConfiguration()
    {
        var contract = new PointContract(handOver: false);
        var values = new MemberAccessors<PointValues>()
            .Member(static (in PointValues v) => v.Label)
            .Member(static (in PointValues v) => v.X, static (ref PointValues v, int m) => v.X = m);

        AssertEx.Throws<BinaryConfigurationException>("no setter", () => contract.HandOver(values));
    }

    [Fact]
    public void UseConstructor_HandedOverTwice_ThrowsConfiguration()
    {
        var contract = new PointContract();

        AssertEx.Throws<BinaryConfigurationException>("already handed over", () => contract.HandOver(PointContract.Values()));
    }

    [Fact]
    public void UseAccessors_AnEntryWithoutASetterAndNoConstructor_ThrowsConfiguration()
    {
        var contract = new PointContract(handOver: false);
        var table = new MemberAccessors<Point>()
            .Member(static (in Point o) => o.Label)
            .Member(static (in Point o) => o.X);

        AssertEx.Throws<BinaryConfigurationException>("UseConstructor", () => contract.HandOverReadOnly(table));
    }
}
