using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using ViShap.Viper.Metadata;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Correctness;

/// <summary>
/// One round trip per supported type family. This is the regression net for the formatter layer:
/// every formatter is exercised through the public API, in both V0 and V1.
/// </summary>
public class RoundTripCorpusTests
{
    private readonly BinarySerializer _serializer = new();

    private T? RoundTrip<T>(T value) => _serializer.Deserialize<T>(_serializer.Serialize(value));

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
        Assert.Equal(string.Empty, RoundTrip(string.Empty));
        Assert.Equal((Half)1.5, RoundTrip((Half)1.5));
        Assert.Equal(Int128.MinValue, RoundTrip(Int128.MinValue));
        Assert.Equal(UInt128.MaxValue, RoundTrip(UInt128.MaxValue));
        Assert.Equal(new IntPtr(42), RoundTrip(new IntPtr(42)));
        Assert.Equal(new UIntPtr(42), RoundTrip(new UIntPtr(42)));
        Assert.Equal(new Rune('A'), RoundTrip(new Rune('A')));
        Assert.Equal(BigInteger.Pow(2, 200), RoundTrip(BigInteger.Pow(2, 200)));
        Assert.Equal(DayOfWeek.Friday, RoundTrip(DayOfWeek.Friday));
    }

    [Fact]
    public void Nullables()
    {
        Assert.Equal(5, RoundTrip<int?>(5));
        Assert.Null(RoundTrip<int?>(null));
        Assert.Null(RoundTrip<string?>(null));
    }

    [Fact]
    public void TimeAndText()
    {
        var now = new DateTime(2026, 9, 18, 12, 30, 0, DateTimeKind.Utc);
        Assert.Equal(now, RoundTrip(now));
        Assert.Equal(new DateTimeOffset(now, TimeSpan.Zero), RoundTrip(new DateTimeOffset(now, TimeSpan.Zero)));
        Assert.Equal(TimeSpan.FromMinutes(90), RoundTrip(TimeSpan.FromMinutes(90)));
        Assert.Equal(new DateOnly(2026, 9, 18), RoundTrip(new DateOnly(2026, 9, 18)));
        Assert.Equal(new TimeOnly(12, 30), RoundTrip(new TimeOnly(12, 30)));
        Assert.Equal(TimeZoneInfo.Utc, RoundTrip(TimeZoneInfo.Utc));
        Assert.Equal("abc", RoundTrip(new StringBuilder("abc"))!.ToString());
        Assert.Equal(CultureInfo.GetCultureInfo("fr-FR"), RoundTrip(CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void SystemTypes()
    {
        var guid = Guid.NewGuid();
        Assert.Equal(guid, RoundTrip(guid));
        Assert.Equal(new Uri("https://example.com/a?b=1"), RoundTrip(new Uri("https://example.com/a?b=1")));
        Assert.Equal(new Version(1, 2, 3, 4), RoundTrip(new Version(1, 2, 3, 4)));

        var bits = new BitArray([true, false, true, true, false]);
        var restored = RoundTrip(bits)!;
        Assert.Equal(bits.Length, restored.Length);
        for (int i = 0; i < bits.Length; i++)
            Assert.Equal(bits[i], restored[i]);
    }

    [Fact]
    public void Numerics()
    {
        Assert.Equal(new Complex(1, 2), RoundTrip(new Complex(1, 2)));
        Assert.Equal(new Vector2(1, 2), RoundTrip(new Vector2(1, 2)));
        Assert.Equal(new Vector3(1, 2, 3), RoundTrip(new Vector3(1, 2, 3)));
        Assert.Equal(new Vector4(1, 2, 3, 4), RoundTrip(new Vector4(1, 2, 3, 4)));
        Assert.Equal(new Quaternion(1, 2, 3, 4), RoundTrip(new Quaternion(1, 2, 3, 4)));
        Assert.Equal(new Plane(1, 2, 3, 4), RoundTrip(new Plane(1, 2, 3, 4)));
        Assert.Equal(Matrix3x2.Identity, RoundTrip(Matrix3x2.Identity));
        Assert.Equal(Matrix4x4.Identity, RoundTrip(Matrix4x4.Identity));
    }

    [Fact]
    public void Arrays()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new[] { 1, 2, 3 }));
        Assert.Equal([], RoundTrip(Array.Empty<int>()));
        Assert.Equal(["a", null, "c"], RoundTrip(new[] { "a", null, "c" }));

        var jagged = new[] { new[] { 1, 2 }, new[] { 3 } };
        var restoredJagged = RoundTrip(jagged)!;
        Assert.Equal(jagged[0], restoredJagged[0]);
        Assert.Equal(jagged[1], restoredJagged[1]);

        var grid = new int[2, 3];
        grid[1, 2] = 42;
        var restoredGrid = RoundTrip(grid)!;
        Assert.Equal(42, restoredGrid[1, 2]);
        Assert.Equal(2, restoredGrid.GetLength(0));
        Assert.Equal(3, restoredGrid.GetLength(1));
    }

    [Fact]
    public void Collections()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new List<int> { 1, 2, 3 }));
        Assert.Equal([1, 2, 3], RoundTrip<IList<int>>([1, 2, 3]));
        Assert.Equal([1, 2, 3], RoundTrip<IEnumerable<int>>([1, 2, 3]));
        Assert.Equal([1, 2, 3], RoundTrip(new HashSet<int> { 1, 2, 3 }).OrderBy(x => x));
        Assert.Equal([1, 2, 3], RoundTrip(new SortedSet<int> { 3, 1, 2 }).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new LinkedList<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ObservableCollection<int> { 1, 2, 3 }).ToArray());
        Assert.Equal([3, 2, 1], RoundTrip(new Stack<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new Queue<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyCollection<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyObservableCollection<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ConcurrentQueue<int>([1, 2, 3])).ToArray());
        Assert.Equal([3, 2, 1], RoundTrip(new ConcurrentStack<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ConcurrentBag<int>([1, 2, 3])).OrderBy(x => x));
    }

    [Fact]
    public void Dictionaries()
    {
        Assert.Equal(new Dictionary<string, int> { ["a"] = 1 }, RoundTrip(new Dictionary<string, int> { ["a"] = 1 }));
        Assert.Equal(new SortedDictionary<string, int> { ["a"] = 1 }, RoundTrip(new SortedDictionary<string, int> { ["a"] = 1 }));
        Assert.Equal(new SortedList<string, int> { ["a"] = 1 }, RoundTrip(new SortedList<string, int> { ["a"] = 1 }));
        Assert.Equal(1, RoundTrip(new ConcurrentDictionary<string, int>([new("a", 1)]))!["a"]);
        Assert.Equal(1, RoundTrip<IReadOnlyDictionary<string, int>>(new Dictionary<string, int> { ["a"] = 1 })!["a"]);
        Assert.Equal(1, RoundTrip(new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["a"] = 1 }))!["a"]);
        Assert.Equal(1, RoundTrip(new Dictionary<string, int> { ["a"] = 1 }.ToFrozenDictionary())!["a"]);
    }

    [Fact]
    public void ImmutableAndFrozen()
    {
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableArray.Create(1, 2, 3)).ToArray());
        Assert.True(RoundTrip(default(ImmutableArray<int>)).IsDefault);
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableList.Create(1, 2, 3)).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableHashSet.Create(1, 2, 3)).OrderBy(x => x));
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableSortedSet.Create(3, 1, 2)).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableQueue.Create(1, 2, 3)).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableStack.Create(3, 2, 1)).ToArray());
        Assert.Equal(1, RoundTrip(ImmutableDictionary<string, int>.Empty.Add("a", 1))!["a"]);
        Assert.Equal(1, RoundTrip(ImmutableSortedDictionary<string, int>.Empty.Add("a", 1))!["a"]);
        Assert.Equal([1, 2, 3], RoundTrip(new[] { 1, 2, 3 }.ToFrozenSet()).OrderBy(x => x));
    }

    [Fact]
    public void MemoryLike()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new Memory<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyMemory<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ArraySegment<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlySequence<int>([1, 2, 3])).ToArray());
    }

    [Fact]
    public void TuplesAndPairs()
    {
        Assert.Equal((1, "a"), RoundTrip((1, "a")));
        Assert.Equal(Tuple.Create(1, "a"), RoundTrip(Tuple.Create(1, "a")));
        Assert.Equal(new KeyValuePair<string, int>("a", 1), RoundTrip(new KeyValuePair<string, int>("a", 1)));
        Assert.Equal(7, RoundTrip(new Lazy<int>(() => 7))!.Value);
    }

    [Fact]
    public void PriorityQueue_PreservesEntries()
    {
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);

        var restored = RoundTrip(queue)!;

        Assert.Equal(2, restored.Count);
        Assert.Equal("high", restored.Dequeue());
        Assert.Equal("low", restored.Dequeue());
    }

    [Fact]
    public void Objects()
    {
        var person = new Person { Name = "Alice", Age = 30 };
        Assert.Equivalent(person, RoundTrip(person));
        Assert.Null(RoundTrip<Person?>(null));

        var nested = new Cyclic { Name = "a", Next = new Cyclic { Name = "b" } };
        var restored = RoundTrip(nested)!;
        Assert.Equal("b", restored.Next!.Name);
        Assert.Null(restored.Next.Next);
    }

    [Fact]
    public void Delegates_AreRejected()
    {
        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize<Func<int>>(() => 1));
    }

    [Fact]
    public void V0_RoundTripsPositionalData()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

        var person = new Person { Name = "Alice", Age = 30 };
        var result = serializer.Deserialize<Person>(serializer.Serialize(person));

        Assert.Equivalent(person, result);
    }

    [Fact]
    public void V0_RejectsKeyedContracts()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

        Assert.Throws<BinaryFormatNotSupportedException>(
            () => serializer.Serialize(new OldSchema { Kept = new Node { Value = 1 } }));
    }

    [Fact]
    public void Inspector_ReadsHeaderWithoutConsumingTheStream()
    {
        using var stream = new MemoryStream();
        _serializer.Serialize(stream, new Person { Name = "Alice", Age = 30 });
        stream.Position = 0;

        var info = BinaryFormatInspector.Peek(stream);

        Assert.NotNull(info);
        Assert.Equal(1, info!.Value.FormatVersion);
        Assert.Equal(0, stream.Position);
        Assert.Equivalent(
            new Person { Name = "Alice", Age = 30 },
            _serializer.Deserialize<Person>(stream));
    }
}
