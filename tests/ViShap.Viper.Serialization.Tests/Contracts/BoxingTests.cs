using System.Buffers;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins TYP-02 (INV-17): the engine boxes only in a polymorphic slot. A boxed value is a heap
/// allocation, so the allocation counter of the current thread is the double that observes it: a
/// graph of value types written or read at two sizes must cost the same beyond the values the read
/// returns, whatever the number of members, enums, nullables and nested structs it holds. The last
/// test turns the counter on a polymorphic slot over a struct, where boxing is the contract, to show
/// the counter sees it when it happens.
/// </summary>
public class BoxingTests
{
    /// <summary>Fewer bytes than one boxed value per element could account for.</summary>
    private const long NotPerElement = 1024;

    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Serialize_AGraphOfValueTypes_BoxesNothing()
    {
        long small = WriteCost(Probes(16));
        long large = WriteCost(Probes(4096));

        Assert.True(
            large - small < NotPerElement,
            $"Writing 4 080 more elements cost {large - small:N0} more bytes.");
    }

    [Fact]
    public void Deserialize_AGraphOfValueTypes_AllocatesOnlyTheArrayItReturns()
    {
        byte[] small = _serializer.Serialize(Probes(16));
        byte[] large = _serializer.Serialize(Probes(4096));

        long difference = ReadCost<Probe[]>(large) - ReadCost<Probe[]>(small);
        long arrays = Allocated(() => _ = new Probe[4096]) - Allocated(() => _ = new Probe[16]);

        Assert.True(
            Math.Abs(difference - arrays) < NotPerElement,
            $"Reading 4 080 more elements cost {difference:N0} more bytes; their array costs {arrays:N0}.");
    }

    [Fact]
    public void SerializeAndDeserialize_Enums_BoxNothing()
    {
        var few = Enumerable.Range(0, 16).Select(i => (Colour)(i % 3)).ToArray();
        var many = Enumerable.Range(0, 4096).Select(i => (Colour)(i % 3)).ToArray();

        Assert.True(WriteCost(many) - WriteCost(few) < NotPerElement);

        long difference = ReadCost<Colour[]>(_serializer.Serialize(many)) - ReadCost<Colour[]>(_serializer.Serialize(few));
        long arrays = Allocated(() => _ = new Colour[4096]) - Allocated(() => _ = new Colour[16]);

        Assert.True(Math.Abs(difference - arrays) < NotPerElement);
    }

    [Fact]
    public void Deserialize_AStructRoot_IsNotBoxedOnItsWayOut()
    {
        byte[] payload = _serializer.Serialize(new Probe { Number = 4, Colour = Colour.Blue });
        byte[] scalar = _serializer.Serialize(4);

        // The same frame machinery reads both; a struct root boxed on its way to the caller would cost
        // one more object than an int root does.
        Assert.Equal(ReadCost<int>(scalar), ReadCost<Probe>(payload));
    }

    [Fact]
    public void Serialize_AStructInAPolymorphicSlot_IsBoxedThere()
    {
        // The positive control: an interface-typed slot holding a struct is the one place the engine
        // boxes, and the counter sees each box.
        var few = Enumerable.Range(0, 16).Select(i => (IShapeLike)new SquareLike { Side = i }).ToArray();
        var many = Enumerable.Range(0, 4096).Select(i => (IShapeLike)new SquareLike { Side = i }).ToArray();

        long difference = ReadCost<IShapeLike[]>(_serializer.Serialize(many)) - ReadCost<IShapeLike[]>(_serializer.Serialize(few));

        Assert.True(difference > 4080 * 16, $"Reading 4 080 more boxed structs cost only {difference:N0} more bytes.");
    }

    public enum Colour : byte
    {
        Red,
        Green,
        Blue
    }

    public struct Inner
    {
        public long Ticks { get; set; }

        public Colour Colour { get; set; }
    }

    /// <summary>A value type with no member that allocates: every field a box would show up in.</summary>
    public struct Probe
    {
        public int Number { get; set; }

        public double Ratio { get; set; }

        public bool Flag { get; set; }

        public Colour Colour { get; set; }

        public DayOfWeek Day { get; set; }

        public int? Maybe { get; set; }

        public Colour? MaybeColour { get; set; }

        public decimal Amount { get; set; }

        public Guid Id { get; set; }

        public DateTime When { get; set; }

        public Inner Inner { get; set; }

        public KeyValuePair<int, long> Pair { get; set; }

        public (int, Colour) Tuple { get; set; }
    }

    [BinaryUnion(1, typeof(SquareLike))]
    public interface IShapeLike;

    public struct SquareLike : IShapeLike
    {
        public int Side { get; set; }
    }

    private static Probe[] Probes(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i => new Probe
        {
            Number = i,
            Ratio = i / 3.0,
            Flag = i % 2 == 0,
            Colour = (Colour)(i % 3),
            Day = (DayOfWeek)(i % 7),
            Maybe = i % 4 == 0 ? null : i,
            MaybeColour = i % 5 == 0 ? null : Colour.Green,
            Amount = i * 1.5m,
            Id = new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
            When = new DateTime(2026, 1, 1).AddMinutes(i),
            Inner = new Inner { Ticks = i, Colour = Colour.Blue },
            Pair = new KeyValuePair<int, long>(i, -i),
            Tuple = (i, Colour.Red)
        })
    ];

    private long WriteCost<T>(T value)
    {
        var destination = new ArrayBufferWriter<byte>(1 << 20);
        return Allocated(() =>
        {
            destination.ResetWrittenCount();
            _serializer.Serialize(destination, value);
        });
    }

    private long ReadCost<T>(byte[] payload) => Allocated(() => _serializer.Deserialize<T>(payload));

    /// <summary>
    /// The bytes one call allocates on this thread once it is warm: the least of several calls, since
    /// a pool the collector trimmed in the meantime can only add to a call, never take from it.
    /// </summary>
    private static long Allocated(Action act)
    {
        for (int i = 0; i < 4; i++)
            act();

        long least = long.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            act();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return least;
    }
}
