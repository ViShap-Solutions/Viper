using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

public sealed class CollectionRoundTripTests
{
    [Fact] public void ListExactTypeAndContents()
    {
        var value = new List<int> { 1, 2, 3 };
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<List<int>>(actual);
        TestHelpers.AssertSequence(value, actual);
    }

    [Fact] public void HashSetSemanticContents()
    {
        var value = new HashSet<int> { 1, 2, 3 };
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<HashSet<int>>(actual);
        Assert.Equal(value.Count, actual.Count);
        Assert.All(value, x => Assert.Contains(x, actual));
    }

    [Fact] public void SortedSetOrderedContents()
    {
        var value = new SortedSet<int> { 3, 1, 2 };
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<SortedSet<int>>(actual);
        TestHelpers.AssertSequence(value, actual);
    }

    [Fact] public void LinkedListOrderedContents()
    {
        var value = new LinkedList<int>(new[] { 3, 1, 2 });
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<LinkedList<int>>(actual);
        TestHelpers.AssertSequence(value, actual);
    }

    [Fact] public void ObservableCollectionOrderedContents()
    {
        var value = new ObservableCollection<string>(new[] { "a", "б", "😀" });
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<ObservableCollection<string>>(actual);
        TestHelpers.AssertSequence(value, actual);
    }

    [Fact] public void StackLifoSemantics()
    {
        var value = new Stack<int>();
        value.Push(1); value.Push(2); value.Push(3);
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<Stack<int>>(actual);
        while (value.Count > 0) Assert.Equal(value.Pop(), actual.Pop());
    }

    [Fact] public void QueueFifoSemantics()
    {
        var value = new Queue<int>(new[] { 1, 2, 3 });
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<Queue<int>>(actual);
        while (value.Count > 0) Assert.Equal(value.Dequeue(), actual.Dequeue());
    }

    [Fact] public void PriorityQueueUsesDequeueSemantics()
    {
        var value = new PriorityQueue<string, int>();
        value.Enqueue("third", 3); value.Enqueue("first", 1); value.Enqueue("second", 2);
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<PriorityQueue<string, int>>(actual);
        while (value.Count > 0)
        {
            var e = value.Dequeue();
            var a = actual.Dequeue();
            Assert.Equal(e, a);
        }
    }

    [Fact] public void ConcurrentCollectionsSemanticContents()
    {
        var bag = TestHelpers.RoundTrip(new ConcurrentBag<int>(new[] { 1, 2, 3 }));
        Assert.IsType<ConcurrentBag<int>>(bag); Assert.Equal(new[] { 1, 2, 3 }, bag.OrderBy(x => x).ToArray());
        var queue = TestHelpers.RoundTrip(new ConcurrentQueue<int>(new[] { 1, 2, 3 }));
        Assert.IsType<ConcurrentQueue<int>>(queue); TestHelpers.AssertSequence(new[] { 1, 2, 3 }, queue);
        var stack = TestHelpers.RoundTrip(new ConcurrentStack<int>(new[] { 1, 2, 3 }));
        Assert.IsType<ConcurrentStack<int>>(stack); Assert.Equal(new[] { 3, 2, 1 }, stack.ToArray());
        var dict = TestHelpers.RoundTrip(new ConcurrentDictionary<int,string>(new[] { KeyValuePair.Create(1,"a"), KeyValuePair.Create(2,"b") }));
        Assert.IsType<ConcurrentDictionary<int,string>>(dict); TestHelpers.AssertDictionary(new Dictionary<int,string>{{1,"a"},{2,"b"}}, dict);
    }

    [Fact] public void DictionariesNeverDependOnHashOrder()
    {
        var value = new Dictionary<string, int> { ["z"] = 26, ["a"] = 1, ["m"] = 13 };
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<Dictionary<string,int>>(actual); TestHelpers.AssertDictionary(value, actual);
    }

    [Fact] public void SortedDictionaryAndSortedListPreserveOrder()
    {
        var sd = TestHelpers.RoundTrip(new SortedDictionary<int,string> { [2]="b", [1]="a", [3]="c" });
        Assert.IsType<SortedDictionary<int,string>>(sd); TestHelpers.AssertSequence(new[] {1,2,3}, sd.Keys);
        var sl = TestHelpers.RoundTrip(new SortedList<int,string> { [2]="b", [1]="a", [3]="c" });
        Assert.IsType<SortedList<int,string>>(sl); TestHelpers.AssertSequence(new[] {1,2,3}, sl.Keys);
    }

    [Fact] public void ReadOnlyCollectionsExactType()
    {
        var rc = TestHelpers.RoundTrip(new ReadOnlyCollection<int>(new[] { 1, 2, 3 }));
        Assert.IsType<ReadOnlyCollection<int>>(rc); TestHelpers.AssertSequence(new[] {1,2,3}, rc);
        var rd = TestHelpers.RoundTrip(new ReadOnlyDictionary<int,string>(new Dictionary<int,string>{{1,"a"}}));
        Assert.IsType<ReadOnlyDictionary<int,string>>(rd); Assert.Equal("a", rd[1]);
        var roc = TestHelpers.RoundTrip(new ReadOnlyObservableCollection<int>(new ObservableCollection<int>(new[]{4,5})));
        Assert.IsType<ReadOnlyObservableCollection<int>>(roc); TestHelpers.AssertSequence(new[]{4,5}, roc);
    }

    [Fact] public void ImmutableCollections()
    {
        var list = TestHelpers.RoundTrip(ImmutableList.Create(1,2,3)); Assert.IsType<ImmutableList<int>>(list); TestHelpers.AssertSequence(new[]{1,2,3}, list);
        var arr = TestHelpers.RoundTrip(ImmutableArray.Create(1,2,3)); Assert.Equal(new[]{1,2,3}, arr.ToArray());
        var hs = TestHelpers.RoundTrip(ImmutableHashSet.Create(1,2,3)); Assert.Equal(3, hs.Count); Assert.All(new[]{1,2,3}, x => Assert.True(hs.Contains(x)));
        var ss = TestHelpers.RoundTrip(ImmutableSortedSet.Create(3,1,2)); TestHelpers.AssertSequence(new[]{1,2,3}, ss);
        var st = TestHelpers.RoundTrip(ImmutableStack.Create(1,2,3)); Assert.Equal(new[]{3,2,1}, st.ToArray());
        var q = TestHelpers.RoundTrip(ImmutableQueue.Create(1,2,3)); TestHelpers.AssertSequence(new[]{1,2,3}, q);
        var d = TestHelpers.RoundTrip(ImmutableDictionary.CreateRange(new[]{KeyValuePair.Create(1,"a"),KeyValuePair.Create(2,"b")})); Assert.Equal("a", d[1]); Assert.Equal("b", d[2]);
        var sd = TestHelpers.RoundTrip(ImmutableSortedDictionary.CreateRange(new[]{KeyValuePair.Create(2,"b"),KeyValuePair.Create(1,"a")})); TestHelpers.AssertSequence(new[]{1,2}, sd.Keys);
    }

    [Fact] public void FrozenCollectionsSemanticContents()
    {
        var fd = TestHelpers.RoundTrip(new Dictionary<int,string>{{2,"b"},{1,"a"}}.ToFrozenDictionary());
        Assert.IsAssignableFrom<FrozenDictionary<int,string>>(fd); Assert.Equal("a", fd[1]); Assert.Equal("b", fd[2]);
        var fs = TestHelpers.RoundTrip(new HashSet<int>{3,1,2}.ToFrozenSet());
        Assert.IsAssignableFrom<FrozenSet<int>>(fs); Assert.Equal(3, fs.Count); Assert.All(new[]{1,2,3}, x => Assert.True(fs.Contains(x)));
    }

    [Fact] public void CustomCollection()
    {
        var value = new CustomCollection<int>(); value.Add(1); value.Add(2);
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<CustomCollection<int>>(actual); TestHelpers.AssertSequence(new[]{1,2}, actual);
    }
}
