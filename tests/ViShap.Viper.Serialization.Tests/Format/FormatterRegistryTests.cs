namespace ViShap.Viper.Serialization.Tests.Format;

public sealed class FormatterRegistryTests
{
    [Fact] public void FORMATTER_COUNT_01_RepresentativeTypesResolveToHandlingFormatters()
    {
        var types=new[]{typeof(bool),typeof(int),typeof(string),typeof(DateTime),typeof(Complex),typeof(StringBuilder),typeof(Guid),typeof(int[]),typeof(Memory<int>),typeof(ReadOnlyMemory<int>),typeof(ArraySegment<int>),typeof(ReadOnlySequence<int>),typeof((int,int)),typeof(List<int>),typeof(HashSet<int>),typeof(SortedSet<int>),typeof(LinkedList<int>),typeof(ObservableCollection<int>),typeof(Stack<int>),typeof(Queue<int>),typeof(PriorityQueue<int,int>),typeof(ConcurrentBag<int>),typeof(ConcurrentQueue<int>),typeof(ConcurrentStack<int>),typeof(ConcurrentDictionary<int,int>),typeof(Dictionary<int,int>),typeof(SortedDictionary<int,int>),typeof(SortedList<int,int>),typeof(ReadOnlyCollection<int>),typeof(ReadOnlyDictionary<int,int>),typeof(ImmutableArray<int>),typeof(ImmutableList<int>),typeof(ImmutableHashSet<int>),typeof(ImmutableSortedSet<int>),typeof(ImmutableStack<int>),typeof(ImmutableQueue<int>),typeof(ImmutableDictionary<int,int>),typeof(ImmutableSortedDictionary<int,int>),typeof(FrozenDictionary<int,int>),typeof(FrozenSet<int>),typeof(Action),typeof(object)};
        foreach(var t in types) Assert.True(TypeFormatterRegistry.Resolve(t).CanHandle(t),t.FullName);
    }
    [Fact] public void FORMATTER_COUNT_02_DelegateIsExplicitlyUnsupported() { var f=TypeFormatterRegistry.Resolve(typeof(Action)); Assert.Throws<BinaryTypeException>(()=>f.Write(new BinaryPayloadWriter(new BinaryWriter(new MemoryStream())),new Action(()=>{}),typeof(Action))); }
}
