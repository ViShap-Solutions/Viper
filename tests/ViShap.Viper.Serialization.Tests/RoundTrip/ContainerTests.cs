using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// One round trip per container family of contract §23. Unordered containers assert contents,
/// ordered ones assert order, and the behavioural containers are drained rather than enumerated. The
/// per-type checkpoints of plan §19.3…§19.5 are added in stage M5.
/// </summary>
public class ContainerTests
{
    private readonly BinarySerializer _serializer = new();

    private T? RoundTrip<T>(T value) => _serializer.Deserialize<T>(_serializer.Serialize(value));

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
    public void MemoryLike()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new Memory<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyMemory<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ArraySegment<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlySequence<int>([1, 2, 3])).ToArray());
    }

    [Fact]
    public void OrderedCollections()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new List<int> { 1, 2, 3 }));
        Assert.Equal([1, 2, 3], RoundTrip(new SortedSet<int> { 3, 1, 2 }).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new LinkedList<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ObservableCollection<int> { 1, 2, 3 }).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyCollection<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ReadOnlyObservableCollection<int>([1, 2, 3])).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(new ConcurrentQueue<int>([1, 2, 3])).ToArray());
        Assert.Equal([3, 2, 1], RoundTrip(new ConcurrentStack<int>([1, 2, 3])).ToArray());
    }

    [Fact]
    public void UnorderedCollections()
    {
        AssertEx.SameContents([1, 2, 3], RoundTrip(new HashSet<int> { 1, 2, 3 }));
        AssertEx.SameContents([1, 2, 3], RoundTrip(new ConcurrentBag<int>([1, 2, 3])));
        AssertEx.SameContents([1, 2, 3], RoundTrip(new[] { 1, 2, 3 }.ToFrozenSet()));
    }

    [Fact]
    public void BehaviouralCollections()
    {
        AssertEx.PopsInOrder([3, 2, 1], RoundTrip(new Stack<int>([1, 2, 3])));
        AssertEx.DequeuesInOrder([1, 2, 3], RoundTrip(new Queue<int>([1, 2, 3])));

        var queue = new PriorityQueue<string, int>();
        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);
        AssertEx.DequeuesInPriorityOrder(["high", "low"], RoundTrip(queue));
    }

    [Fact]
    public void Interfaces_ResolveToTheDocumentedConcreteType()
    {
        Assert.Equal(typeof(List<int>), RoundTrip<IList<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<IEnumerable<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<IReadOnlyList<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<ICollection<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(HashSet<int>), RoundTrip<ISet<int>>(new HashSet<int> { 1 })!.GetType());

        Assert.Equal(
            typeof(Dictionary<string, int>),
            RoundTrip<IDictionary<string, int>>(new Dictionary<string, int> { ["a"] = 1 })!.GetType());
        Assert.Equal(
            typeof(ReadOnlyDictionary<string, int>),
            RoundTrip<IReadOnlyDictionary<string, int>>(
                new Dictionary<string, int> { ["a"] = 1 })!.GetType());

        Assert.IsAssignableFrom<IImmutableList<int>>(
            RoundTrip<IImmutableList<int>>(ImmutableList.Create(1, 2)));
        Assert.IsAssignableFrom<IImmutableSet<int>>(
            RoundTrip<IImmutableSet<int>>(ImmutableHashSet.Create(1, 2)));
        Assert.IsAssignableFrom<IImmutableDictionary<string, int>>(
            RoundTrip<IImmutableDictionary<string, int>>(
                ImmutableDictionary<string, int>.Empty.Add("a", 1)));
    }

    [Fact]
    public void Dictionaries()
    {
        Assert.Equal(new Dictionary<string, int> { ["a"] = 1 }, RoundTrip(new Dictionary<string, int> { ["a"] = 1 }));
        Assert.Equal(new SortedDictionary<string, int> { ["a"] = 1 }, RoundTrip(new SortedDictionary<string, int> { ["a"] = 1 }));
        Assert.Equal(new SortedList<string, int> { ["a"] = 1 }, RoundTrip(new SortedList<string, int> { ["a"] = 1 }));
        Assert.Equal(1, RoundTrip(new ConcurrentDictionary<string, int>([new("a", 1)]))!["a"]);
        Assert.Equal(1, RoundTrip(new ReadOnlyDictionary<string, int>(new Dictionary<string, int> { ["a"] = 1 }))!["a"]);
        Assert.Equal(1, RoundTrip(new Dictionary<string, int> { ["a"] = 1 }.ToFrozenDictionary())!["a"]);
    }

    [Fact]
    public void ImmutableAndFrozen()
    {
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableArray.Create(1, 2, 3)).ToArray());
        Assert.True(RoundTrip(default(ImmutableArray<int>)).IsDefault);
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableList.Create(1, 2, 3)).ToArray());
        AssertEx.SameContents([1, 2, 3], RoundTrip(ImmutableHashSet.Create(1, 2, 3)));
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableSortedSet.Create(3, 1, 2)).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableQueue.Create(1, 2, 3)).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(ImmutableStack.Create(3, 2, 1)).ToArray());
        Assert.Equal(1, RoundTrip(ImmutableDictionary<string, int>.Empty.Add("a", 1))!["a"]);
        Assert.Equal(1, RoundTrip(ImmutableSortedDictionary<string, int>.Empty.Add("a", 1))!["a"]);
    }

    [Fact]
    public void EmptyContainers()
    {
        Assert.Empty(RoundTrip(new List<int>())!);
        Assert.Empty(RoundTrip(new Dictionary<string, int>())!);
        Assert.Empty(RoundTrip(new HashSet<int>())!);
        Assert.Empty(RoundTrip(ImmutableList<int>.Empty)!);
        Assert.True(RoundTrip(ImmutableArray<int>.Empty).IsEmpty);
    }

    [Fact]
    public void TuplesAndPairs()
    {
        Assert.Equal((1, "a"), RoundTrip((1, "a")));
        Assert.Equal(Tuple.Create(1, "a"), RoundTrip(Tuple.Create(1, "a")));
        Assert.Equal(new KeyValuePair<string, int>("a", 1), RoundTrip(new KeyValuePair<string, int>("a", 1)));
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
}
