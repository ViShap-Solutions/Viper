using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins V0-20 and V0-21: every family of §23 that V0 supports round-trips through it, and the bytes
/// it produces are the ones a V1 frame carries for the same value (§22.8). V0 is the same engine
/// without a header, so a family that encodes differently under the two formats is a defect.
/// </summary>
public class V0CorpusTests
{
    private readonly BinarySerializer _headerless = new(
        BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    private readonly BinarySerializer _framed = new();

    /// <summary>
    /// Round-trips <paramref name="value"/> through V0 and asserts on the way that the headerless
    /// payload is byte-identical to the one the V1 frame carries.
    /// </summary>
    private T? RoundTrip<T>(T value)
    {
        byte[] payload = _headerless.Serialize(value);

        Assert.Equal(payload, _framed.Serialize(value)[Wire.PlainHeaderLength..]);

        return _headerless.Deserialize<T>(payload);
    }

    [Fact]
    public void Primitives()
    {
        Assert.True(RoundTrip(true));
        Assert.Equal((byte)7, RoundTrip((byte)7));
        Assert.Equal((sbyte)-7, RoundTrip((sbyte)-7));
        Assert.Equal((short)-300, RoundTrip((short)-300));
        Assert.Equal((ushort)300, RoundTrip((ushort)300));
        Assert.Equal(-100_000, RoundTrip(-100_000));
        Assert.Equal(100_000u, RoundTrip(100_000u));
        Assert.Equal(-10_000_000_000L, RoundTrip(-10_000_000_000L));
        Assert.Equal(10_000_000_000UL, RoundTrip(10_000_000_000UL));
        Assert.Equal(3.5f, RoundTrip(3.5f));
        Assert.Equal(3.5e300, RoundTrip(3.5e300));
        Assert.Equal(12345.6789m, RoundTrip(12345.6789m));
        Assert.Equal('ß', RoundTrip('ß'));
        Assert.Equal("héllo ☃", RoundTrip("héllo ☃"));
        Assert.Equal((Half)1.5, RoundTrip((Half)1.5));
        Assert.Equal(Int128.MinValue, RoundTrip(Int128.MinValue));
        Assert.Equal(UInt128.MaxValue, RoundTrip(UInt128.MaxValue));
        Assert.Equal(new IntPtr(42), RoundTrip(new IntPtr(42)));
        Assert.Equal(new UIntPtr(42), RoundTrip(new UIntPtr(42)));
        Assert.Equal(new Rune('A'), RoundTrip(new Rune('A')));
        Assert.Equal(BigInteger.Pow(2, 200), RoundTrip(BigInteger.Pow(2, 200)));
        Assert.Equal(DayOfWeek.Friday, RoundTrip(DayOfWeek.Friday));
        Assert.Equal(5, RoundTrip<int?>(5));
        Assert.Null(RoundTrip<int?>(null));
    }

    [Fact]
    public void Time()
    {
        var moment = new DateTime(2026, 9, 18, 12, 30, 0, DateTimeKind.Utc);

        Assert.Equal(moment, RoundTrip(moment));
        Assert.Equal(new DateTimeOffset(moment), RoundTrip(new DateTimeOffset(moment)));
        Assert.Equal(TimeSpan.FromMinutes(90), RoundTrip(TimeSpan.FromMinutes(90)));
        Assert.Equal(new DateOnly(2026, 9, 18), RoundTrip(new DateOnly(2026, 9, 18)));
        Assert.Equal(new TimeOnly(12, 30), RoundTrip(new TimeOnly(12, 30)));
        Assert.Equal(TimeZoneInfo.Utc, RoundTrip(TimeZoneInfo.Utc));
    }

    [Fact]
    public void Numerics()
    {
        Assert.Equal(new Complex(1.5, -2.5), RoundTrip(new Complex(1.5, -2.5)));
        Assert.Equal(new Vector2(1, 2), RoundTrip(new Vector2(1, 2)));
        Assert.Equal(new Vector3(1, 2, 3), RoundTrip(new Vector3(1, 2, 3)));
        Assert.Equal(new Vector4(1, 2, 3, 4), RoundTrip(new Vector4(1, 2, 3, 4)));
        Assert.Equal(new Quaternion(1, 2, 3, 4), RoundTrip(new Quaternion(1, 2, 3, 4)));
        Assert.Equal(new Plane(1, 2, 3, 4), RoundTrip(new Plane(1, 2, 3, 4)));
        Assert.Equal(new Matrix3x2(1, 2, 3, 4, 5, 6), RoundTrip(new Matrix3x2(1, 2, 3, 4, 5, 6)));
        Assert.Equal(Matrix4x4.Identity, RoundTrip(Matrix4x4.Identity));
    }

    [Fact]
    public void System()
    {
        var id = Guid.Parse("8d0c1b2a-3e4f-5061-7283-94a5b6c7d8e9");

        Assert.Equal(id, RoundTrip(id));
        Assert.Equal(new Uri("https://example.test/a?b=c"), RoundTrip(new Uri("https://example.test/a?b=c")));
        Assert.Equal(new Version(1, 2, 3, 4), RoundTrip(new Version(1, 2, 3, 4)));
        Assert.Equal("text", RoundTrip(new StringBuilder("text"))!.ToString());
        Assert.Equal(CultureInfo.InvariantCulture, RoundTrip(CultureInfo.InvariantCulture));

        var bits = RoundTrip(new BitArray([true, false, true]))!;
        Assert.Equal(3, bits.Length);
        Assert.True(bits[0]);
        Assert.False(bits[1]);
        Assert.True(bits[2]);
    }

    [Fact]
    public void Composites()
    {
        Assert.Equal(new KeyValuePair<string, int>("a", 1), RoundTrip(new KeyValuePair<string, int>("a", 1)));
        Assert.Equal((1, "a"), RoundTrip((1, "a")));
        Assert.Equal(Tuple.Create(1, "a"), RoundTrip(Tuple.Create(1, "a")));
        Assert.Equal(7, RoundTrip(new Lazy<int>(() => 7))!.Value);
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableArray.Create(1, 2, 3)).ToArray());
        Assert.True(RoundTrip(default(ImmutableArray<int>)).IsDefault);

        var grid = new int[2, 3];
        grid[1, 2] = 42;
        var restoredGrid = RoundTrip(grid)!;
        Assert.Equal(42, restoredGrid[1, 2]);
        Assert.Equal(2, restoredGrid.GetLength(0));
        Assert.Equal(3, restoredGrid.GetLength(1));
    }

    [Fact]
    public void ArraysAndMemory()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new[] { 1, 2, 3 })!);
        Assert.Equal([], RoundTrip(Array.Empty<int>())!);
        Assert.Equal(["a", null, "c"], RoundTrip(new[] { "a", null, "c" })!);
        Assert.Equal([1, 2, 3], RoundTrip(new Memory<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyMemory<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ArraySegment<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlySequence<int>([1, 2, 3])).ToArray());
    }

    [Fact]
    public void Collections()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new List<int> { 1, 2, 3 })!);
        Assert.Equal([1, 2, 3], RoundTrip(new SortedSet<int> { 3, 1, 2 })!.ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new LinkedList<int>([1, 2, 3]))!.ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ObservableCollection<int> { 1, 2, 3 })!.ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyCollection<int>([1, 2, 3]))!.ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyObservableCollection<int>([1, 2, 3]))!.ToArray());

        AssertEx.SameContents([1, 2, 3], RoundTrip(new HashSet<int> { 1, 2, 3 }));
        AssertEx.PopsInOrder([3, 2, 1], RoundTrip(new Stack<int>([1, 2, 3])));
        AssertEx.DequeuesInOrder([1, 2, 3], RoundTrip(new Queue<int>([1, 2, 3])));

        Assert.Equal(typeof(List<int>), RoundTrip<IList<int>>([1, 2, 3])!.GetType());
    }

    [Fact]
    public void Concurrent()
    {
        AssertEx.SameContents([1, 2, 3], RoundTrip(new ConcurrentBag<int>([1, 2, 3])));
        Assert.Equal([1, 2, 3], RoundTrip(new ConcurrentQueue<int>([1, 2, 3]))!.ToArray());
        Assert.Equal([3, 2, 1], RoundTrip(new ConcurrentStack<int>([1, 2, 3]))!.ToArray());
        Assert.Equal(1, RoundTrip(new ConcurrentDictionary<string, int>([new("a", 1)]))!["a"]);
    }

    [Fact]
    public void Dictionaries()
    {
        Assert.Equal(1, RoundTrip(new Dictionary<string, int> { ["a"] = 1 })!["a"]);
        Assert.Equal(1, RoundTrip(new SortedDictionary<string, int> { ["a"] = 1 })!["a"]);
        Assert.Equal(1, RoundTrip(new SortedList<string, int> { ["a"] = 1 })!["a"]);
        Assert.Equal(
            1,
            RoundTrip(new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["a"] = 1 }))!["a"]);

        var queue = new PriorityQueue<string, int>();
        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);
        AssertEx.DequeuesInPriorityOrder(["high", "low"], RoundTrip(queue));
    }

    [Fact]
    public void ImmutableAndFrozen()
    {
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableList.Create(1, 2, 3))!.ToArray());
        AssertEx.SameContents([1, 2, 3], RoundTrip(ImmutableHashSet.Create(1, 2, 3)));
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableSortedSet.Create(3, 1, 2))!.ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableQueue.Create(1, 2, 3))!.ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableStack.Create(3, 2, 1))!.ToArray());
        Assert.Equal(1, RoundTrip(ImmutableDictionary<string, int>.Empty.Add("a", 1))!["a"]);
        Assert.Equal(1, RoundTrip(ImmutableSortedDictionary<string, int>.Empty.Add("a", 1))!["a"]);
        AssertEx.SameContents([1, 2, 3], RoundTrip(new[] { 1, 2, 3 }.ToFrozenSet()));
        Assert.Equal(1, RoundTrip(new Dictionary<string, int> { ["a"] = 1 }.ToFrozenDictionary())!["a"]);
    }

    [Fact]
    public void Objects()
    {
        var person = new Person { Name = "Ada", Age = 36 };
        Assert.Equivalent(person, RoundTrip(person));
        Assert.Null(RoundTrip<Person?>(null));

        var keyed = RoundTrip(new NewSchema { Removed = new Node { Value = 1 }, Kept = new Node { Value = 7 } })!;
        Assert.Equal(1, keyed.Removed!.Value);
        Assert.Equal(7, keyed.Kept!.Value);
    }

    [Fact]
    public void Unions()
    {
        var restored = RoundTrip<UnionBase>(new UnionDerived { A = 3, Z = 4 })!;

        Assert.Equal(typeof(UnionDerived), restored.GetType());
        Assert.Equal(3, ((UnionDerived)restored).A);
        Assert.Equal(4, restored.Z);
    }
}
