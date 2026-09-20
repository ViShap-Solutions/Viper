using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Plan §19.5: RT-61…RT-88 and the cross-cutting rules RT-C01…RT-C04, RT-C06 and RT-C07. Ordered
/// containers are asserted in order, unordered ones by contents, and the behavioural ones are
/// drained rather than enumerated, which is the only order their own contract promises.
/// </summary>
public abstract partial class Corpus
{
    // --- ordered sequences ----------------------------------------------------------------------

    [Fact]
    public void Deserialize_List_RestoresTheElementsInOrder()
    {
        Assert.Equal([1, 2, 3], RoundTrip(new List<int> { 1, 2, 3 }));
        Assert.Equal(typeof(List<int>), RoundTrip(new List<int> { 1 })!.GetType());
    }

    [Fact]
    public void Deserialize_SortedSet_RestoresTheElementsInSortOrder()
    {
        var restored = RoundTrip(new SortedSet<int> { 3, 1, 2 })!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(typeof(SortedSet<int>), restored.GetType());
    }

    [Fact]
    public void Deserialize_LinkedList_RestoresTheElementsInOrder()
    {
        var restored = RoundTrip(new LinkedList<int>([1, 2, 3]))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(1, restored.First!.Value);
        Assert.Equal(3, restored.Last!.Value);
    }

    [Fact]
    public void Deserialize_ObservableCollection_RestoresTheElementsInOrder() =>
        Assert.Equal([1, 2, 3], RoundTrip(new ObservableCollection<int> { 1, 2, 3 })!.ToArray());

    [Fact]
    public void Deserialize_ReadOnlyCollection_RestoresTheElementsInOrder()
    {
        var restored = RoundTrip(new ReadOnlyCollection<int>([1, 2, 3]))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(typeof(ReadOnlyCollection<int>), restored.GetType());
    }

    [Fact]
    public void Deserialize_ReadOnlyObservableCollection_RestoresTheElementsInOrder()
    {
        var restored = RoundTrip(new ReadOnlyObservableCollection<int>([1, 2, 3]))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(typeof(ReadOnlyObservableCollection<int>), restored.GetType());
    }

    [Fact]
    public void Deserialize_CustomCollection_RestoresTheElementsThroughItsOwnAdd()
    {
        var restored = RoundTrip(new Bag<string> { "a", "b" })!;

        Assert.Equal(typeof(Bag<string>), restored.GetType());
        Assert.Equal(["a", "b"], restored.ToArray());
    }

    // --- unordered and behavioural --------------------------------------------------------------

    [Fact]
    public void Deserialize_HashSet_RestoresTheContents()
    {
        var restored = RoundTrip(new HashSet<int> { 1, 2, 3 })!;

        AssertEx.SameContents([1, 2, 3], restored);
        Assert.Equal(typeof(HashSet<int>), restored.GetType());
    }

    [Fact]
    public void Deserialize_Stack_RestoresThePopOrder()
    {
        // RT-C02: a stack is written bottom-up, so popping the restored one repeats the original.
        AssertEx.PopsInOrder([3, 2, 1], RoundTrip(new Stack<int>([1, 2, 3])));
        Assert.Equal([3, 2, 1], RoundTrip(new Stack<int>([1, 2, 3]))!.ToArray());
    }

    [Fact]
    public void Deserialize_Queue_RestoresTheDequeueOrder()
    {
        AssertEx.DequeuesInOrder([1, 2, 3], RoundTrip(new Queue<int>([1, 2, 3])));
        Assert.Equal([1, 2, 3], RoundTrip(new Queue<int>([1, 2, 3]))!.ToArray());
    }

    [Fact]
    public void Deserialize_ConcurrentBag_RestoresTheContents()
    {
        var restored = RoundTrip(new ConcurrentBag<int>([1, 2, 3]))!;

        AssertEx.SameContents([1, 2, 3], restored);
        Assert.Equal(typeof(ConcurrentBag<int>), restored.GetType());
    }

    [Fact]
    public void Deserialize_ConcurrentQueue_RestoresTheElementsInOrder()
    {
        var restored = RoundTrip(new ConcurrentQueue<int>([1, 2, 3]))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.True(restored.TryDequeue(out int first));
        Assert.Equal(1, first);
    }

    [Fact]
    public void Deserialize_ConcurrentStack_RestoresTheTopFirst()
    {
        // RT-C02 again: the elements go on the wire bottom-up, so the top comes back on top.
        var restored = RoundTrip(new ConcurrentStack<int>([1, 2, 3]))!;

        Assert.Equal([3, 2, 1], restored.ToArray());
        Assert.True(restored.TryPop(out int top));
        Assert.Equal(3, top);
    }

    // --- dictionaries ---------------------------------------------------------------------------

    [Fact]
    public void Deserialize_Dictionary_RestoresEveryEntry()
    {
        var restored = RoundTrip(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 })!;

        Assert.Equal(typeof(Dictionary<string, int>), restored.GetType());
        Assert.Equal(2, restored.Count);
        Assert.Equal(1, restored["a"]);
        Assert.Equal(2, restored["b"]);
    }

    [Fact]
    public void Deserialize_DictionaryWithNullValues_RestoresThem()
    {
        var restored = RoundTrip(new Dictionary<string, string?> { ["a"] = null })!;

        Assert.True(restored.ContainsKey("a"));
        Assert.Null(restored["a"]);
    }

    [Fact]
    public void Deserialize_SortedDictionary_RestoresTheEntriesInKeyOrder()
    {
        var restored = RoundTrip(new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 })!;

        Assert.Equal(typeof(SortedDictionary<string, int>), restored.GetType());
        Assert.Equal(["a", "b"], restored.Keys.ToArray());
    }

    [Fact]
    public void Deserialize_SortedList_RestoresTheEntriesInKeyOrder()
    {
        var restored = RoundTrip(new SortedList<string, int> { ["b"] = 2, ["a"] = 1 })!;

        Assert.Equal(typeof(SortedList<string, int>), restored.GetType());
        Assert.Equal(["a", "b"], restored.Keys.ToArray());
        Assert.Equal(2, restored["b"]);
    }

    [Fact]
    public void Deserialize_ConcurrentDictionary_RestoresEveryEntry()
    {
        var restored = RoundTrip(new ConcurrentDictionary<string, int>(
            [new("a", 1), new("b", 2)]))!;

        Assert.Equal(typeof(ConcurrentDictionary<string, int>), restored.GetType());
        Assert.Equal(1, restored["a"]);
        Assert.Equal(2, restored["b"]);
    }

    [Fact]
    public void Deserialize_ReadOnlyDictionary_RestoresEveryEntry()
    {
        var restored = RoundTrip(new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int> { ["a"] = 1 }))!;

        Assert.Equal(typeof(ReadOnlyDictionary<string, int>), restored.GetType());
        Assert.Equal(1, restored["a"]);
    }

    [Fact]
    public void Deserialize_PriorityQueue_ReconstructsTheDequeueOrderFromThePriorities()
    {
        // RT-79: the pairs travel unordered, so the order is rebuilt by the priorities, not copied.
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);
        queue.Enqueue("middle", 5);

        var restored = RoundTrip(queue)!;

        Assert.Equal(3, restored.Count);
        AssertEx.DequeuesInPriorityOrder(["high", "middle", "low"], restored);
    }

    // --- immutable and frozen -------------------------------------------------------------------

    [Fact]
    public void Deserialize_ImmutableList_RestoresTheElementsInOrder()
    {
        var restored = RoundTrip(ImmutableList.Create(1, 2, 3))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.IsType<ImmutableList<int>>(restored);
    }

    [Fact]
    public void Deserialize_ImmutableHashSet_RestoresTheContents()
    {
        var restored = RoundTrip(ImmutableHashSet.Create(1, 2, 3))!;

        AssertEx.SameContents([1, 2, 3], restored);
        Assert.IsType<ImmutableHashSet<int>>(restored);
    }

    [Fact]
    public void Deserialize_ImmutableSortedSet_RestoresTheElementsInSortOrder()
    {
        var restored = RoundTrip(ImmutableSortedSet.Create(3, 1, 2))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.IsType<ImmutableSortedSet<int>>(restored);
    }

    [Fact]
    public void Deserialize_ImmutableStack_RestoresThePopOrder()
    {
        // ImmutableStack.Create(3, 2, 1) pushes 3 first, so 1 is on top and comes back on top.
        var restored = RoundTrip(ImmutableStack.Create(3, 2, 1))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(1, restored.Peek());
    }

    [Fact]
    public void Deserialize_ImmutableQueue_RestoresTheDequeueOrder()
    {
        var restored = RoundTrip(ImmutableQueue.Create(1, 2, 3))!;

        Assert.Equal([1, 2, 3], restored.ToArray());
        Assert.Equal(1, restored.Peek());
    }

    [Fact]
    public void Deserialize_ImmutableDictionary_RestoresEveryEntry()
    {
        var restored = RoundTrip(
            ImmutableDictionary<string, int>.Empty.Add("a", 1).Add("b", 2))!;

        Assert.Equal(2, restored.Count);
        Assert.Equal(1, restored["a"]);
    }

    [Fact]
    public void Deserialize_ImmutableSortedDictionary_RestoresTheEntriesInKeyOrder()
    {
        var restored = RoundTrip(
            ImmutableSortedDictionary<string, int>.Empty.Add("b", 2).Add("a", 1))!;

        Assert.Equal(["a", "b"], restored.Keys.ToArray());
        Assert.Equal(2, restored["b"]);
    }

    [Fact]
    public void Deserialize_FrozenSet_RestoresTheContents()
    {
        var restored = RoundTrip(new[] { 1, 2, 3 }.ToFrozenSet())!;

        AssertEx.SameContents([1, 2, 3], restored);
        Assert.IsAssignableFrom<FrozenSet<int>>(restored);
    }

    [Fact]
    public void Deserialize_FrozenDictionary_RestoresEveryEntry()
    {
        var restored = RoundTrip(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }.ToFrozenDictionary())!;

        Assert.IsAssignableFrom<FrozenDictionary<string, int>>(restored);
        Assert.Equal(2, restored.Count);
        Assert.Equal(1, restored["a"]);
    }

    // --- cross-cutting --------------------------------------------------------------------------

    [Fact]
    public void Deserialize_EmptyContainer_RestoresItEmptyForEveryFamily()
    {
        // RT-C01: an empty container is a zero count, which is valid wherever the shape allows it.
        Assert.Empty(RoundTrip(new List<int>())!);
        Assert.Empty(RoundTrip(new HashSet<int>())!);
        Assert.Empty(RoundTrip(new SortedSet<int>())!);
        Assert.Empty(RoundTrip(new LinkedList<int>())!);
        Assert.Empty(RoundTrip(new ObservableCollection<int>())!);
        Assert.Empty(RoundTrip(new Stack<int>())!);
        Assert.Empty(RoundTrip(new Queue<int>())!);
        Assert.Empty(RoundTrip(new ReadOnlyCollection<int>([]))!);
        Assert.Empty(RoundTrip(new ReadOnlyObservableCollection<int>([]))!);
        Assert.Empty(RoundTrip(new Bag<int>())!);
        Assert.Empty(RoundTrip(new ConcurrentBag<int>())!);
        Assert.Empty(RoundTrip(new ConcurrentQueue<int>())!);
        Assert.Empty(RoundTrip(new ConcurrentStack<int>())!);
        Assert.Empty(RoundTrip(new Dictionary<string, int>())!);
        Assert.Empty(RoundTrip(new SortedDictionary<string, int>())!);
        Assert.Empty(RoundTrip(new SortedList<string, int>())!);
        Assert.Empty(RoundTrip(new ConcurrentDictionary<string, int>())!);
        Assert.Empty(RoundTrip(new ReadOnlyDictionary<string, int>(new Dictionary<string, int>()))!);
        Assert.Empty(RoundTrip(ImmutableList<int>.Empty)!);
        Assert.Empty(RoundTrip(ImmutableHashSet<int>.Empty)!);
        Assert.Empty(RoundTrip(ImmutableSortedSet<int>.Empty)!);
        Assert.Empty(RoundTrip(ImmutableStack<int>.Empty)!);
        Assert.Empty(RoundTrip(ImmutableQueue<int>.Empty)!);
        Assert.Empty(RoundTrip(ImmutableDictionary<string, int>.Empty)!);
        Assert.Empty(RoundTrip(ImmutableSortedDictionary<string, int>.Empty)!);
        Assert.Empty(RoundTrip(Array.Empty<int>().ToFrozenSet())!);
        Assert.Empty(RoundTrip(new Dictionary<string, int>().ToFrozenDictionary())!);
        Assert.Equal(0, RoundTrip(new PriorityQueue<string, int>())!.Count);
        Assert.True(RoundTrip(ImmutableArray<int>.Empty).IsEmpty);
    }

    [Fact]
    public void Deserialize_InterfaceTypedValue_ResolvesToTheDocumentedConcreteType()
    {
        // RT-C04: §23 fixes what each interface produces on read; it is part of the contract, not an
        // implementation detail a caller may not rely on.
        Assert.Equal(typeof(List<int>), RoundTrip<IList<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<ICollection<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<IEnumerable<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<IReadOnlyList<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(List<int>), RoundTrip<IReadOnlyCollection<int>>([1, 2, 3])!.GetType());
        Assert.Equal(typeof(HashSet<int>), RoundTrip<ISet<int>>(new HashSet<int> { 1 })!.GetType());

        Assert.Equal(
            typeof(Dictionary<string, int>),
            RoundTrip<IDictionary<string, int>>(new Dictionary<string, int> { ["a"] = 1 })!.GetType());
        Assert.Equal(
            typeof(ReadOnlyDictionary<string, int>),
            RoundTrip<IReadOnlyDictionary<string, int>>(
                new Dictionary<string, int> { ["a"] = 1 })!.GetType());

        Assert.IsType<ImmutableList<int>>(RoundTrip<IImmutableList<int>>(ImmutableList.Create(1, 2)));
        Assert.IsType<ImmutableHashSet<int>>(
            RoundTrip<IImmutableSet<int>>(ImmutableHashSet.Create(1, 2)));
        Assert.IsAssignableFrom<IImmutableQueue<int>>(
            RoundTrip<IImmutableQueue<int>>(ImmutableQueue.Create(1, 2)));
        Assert.IsAssignableFrom<IImmutableStack<int>>(
            RoundTrip<IImmutableStack<int>>(ImmutableStack.Create(1, 2)));
        Assert.IsAssignableFrom<IImmutableDictionary<string, int>>(
            RoundTrip<IImmutableDictionary<string, int>>(
                ImmutableDictionary<string, int>.Empty.Add("a", 1)));
    }

    [Fact]
    public void Deserialize_DefaultContainerState_SurvivesOnlyForImmutableArray()
    {
        // RT-C06: ImmutableArray carries a presence flag (§22.5); every other container is written
        // as its elements, so a "default" instance comes back as an ordinary empty one.
        Assert.True(RoundTrip(default(ImmutableArray<int>)).IsDefault);
        Assert.False(RoundTrip(ImmutableArray<int>.Empty).IsDefault);

        // §23 makes a default segment an empty one: it has no backing array to write, and the read
        // builds the array every segment gets.
        Assert.Null(default(ArraySegment<int>).Array);
        Assert.NotNull(RoundTrip(default(ArraySegment<int>)).Array);
        Assert.Empty(RoundTrip(default(ArraySegment<int>)));

        Assert.True(default(Memory<int>).IsEmpty);
        Assert.True(RoundTrip(default(Memory<int>)).IsEmpty);
    }

    [Fact]
    public void Deserialize_NestedContainers_RestoresEveryLevel()
    {
        // RT-C07: a collection of dictionaries, a dictionary of collections, and an array of objects.
        var listOfMaps = RoundTrip(new List<Dictionary<string, int>>
        {
            new() { ["a"] = 1 },
            new() { ["b"] = 2, ["c"] = 3 }
        })!;

        Assert.Equal(2, listOfMaps.Count);
        Assert.Equal(1, listOfMaps[0]["a"]);
        Assert.Equal(3, listOfMaps[1]["c"]);

        var mapOfLists = RoundTrip(new Dictionary<string, List<int>>
        {
            ["odd"] = [1, 3],
            ["even"] = []
        })!;

        Assert.Equal([1, 3], mapOfLists["odd"]);
        Assert.Empty(mapOfLists["even"]);

        var people = RoundTrip(new[]
        {
            new Person { Name = "Ada", Age = 36 },
            new Person { Name = "Grace", Age = 45 }
        })!;

        Assert.Equal("Grace", people[1].Name);

        var deep = RoundTrip(new Dictionary<int, List<Dictionary<string, int[]>>>
        {
            [1] = [new Dictionary<string, int[]> { ["x"] = [7, 8] }]
        })!;

        Assert.Equal([7, 8], deep[1][0]["x"]);
    }
}
