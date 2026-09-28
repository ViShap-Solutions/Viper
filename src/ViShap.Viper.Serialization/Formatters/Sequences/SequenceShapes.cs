using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Formatters;

/// <summary>
/// Defaults shared by the sequence shapes. The count is a collection count, the elements travel in
/// enumeration order, and the collection is built in place unless a shape says otherwise.
/// </summary>
internal abstract class SequenceShape<TCollection, TElement, TBuilder, TEnumerator>
    : ISequenceShape<TCollection, TElement, TBuilder, TEnumerator>
    where TEnumerator : IEnumerator<TElement>
{
    public virtual CountKind CountKind => CountKind.Collection;

    public virtual string CountName => "Collection count";

    public virtual bool ReverseOnWrite => false;

    public virtual bool BuilderIsInstance => true;

    public abstract int? CountOf(TCollection collection);

    public abstract TEnumerator GetEnumerator(TCollection collection);

    public abstract TBuilder Create(int capacity);

    public abstract void Add(ref TBuilder builder, TElement element);

    public abstract TCollection Complete(TBuilder builder);
}

/// <summary>The O(1) count of a collection known only through an interface, when it has one.</summary>
internal static class Counts
{
    public static int? Of<TElement>(IEnumerable<TElement> collection) => collection switch
    {
        ICollection counted => counted.Count,
        ICollection<TElement> counted => counted.Count,
        IReadOnlyCollection<TElement> counted => counted.Count,
        _ => null
    };
}

internal sealed class ListShape<T> : SequenceShape<List<T>, T, List<T>, List<T>.Enumerator>
{
    public override int? CountOf(List<T> collection) => collection.Count;
    public override List<T>.Enumerator GetEnumerator(List<T> collection) => collection.GetEnumerator();
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override List<T> Complete(List<T> builder) => builder;
}

/// <summary>
/// <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>,
/// <c>IReadOnlyList&lt;T&gt;</c> and <c>IReadOnlyCollection&lt;T&gt;</c>: any implementation is
/// written, and a <c>List&lt;T&gt;</c> is read.
/// </summary>
internal sealed class ListInterfaceShape<TCollection, T> : SequenceShape<TCollection, T, List<T>, IEnumerator<T>>
    where TCollection : class, IEnumerable<T>
{
    public override int? CountOf(TCollection collection) => Counts.Of(collection);
    public override IEnumerator<T> GetEnumerator(TCollection collection) => collection.GetEnumerator();
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override TCollection Complete(List<T> builder) => (TCollection)(object)builder;
}

internal sealed class HashSetShape<T> : SequenceShape<HashSet<T>, T, HashSet<T>, HashSet<T>.Enumerator>
{
    public override int? CountOf(HashSet<T> collection) => collection.Count;
    public override HashSet<T>.Enumerator GetEnumerator(HashSet<T> collection) => collection.GetEnumerator();
    public override HashSet<T> Create(int capacity) => new(capacity);
    public override void Add(ref HashSet<T> builder, T element) => builder.Add(element);
    public override HashSet<T> Complete(HashSet<T> builder) => builder;
}

/// <summary><c>ISet&lt;T&gt;</c>: any implementation is written, and a <c>HashSet&lt;T&gt;</c> is read.</summary>
internal sealed class SetInterfaceShape<T> : SequenceShape<ISet<T>, T, HashSet<T>, IEnumerator<T>>
{
    public override int? CountOf(ISet<T> collection) => Counts.Of(collection);
    public override IEnumerator<T> GetEnumerator(ISet<T> collection) => collection.GetEnumerator();
    public override HashSet<T> Create(int capacity) => new(capacity);
    public override void Add(ref HashSet<T> builder, T element) => builder.Add(element);
    public override ISet<T> Complete(HashSet<T> builder) => builder;
}

internal sealed class SortedSetShape<T> : SequenceShape<SortedSet<T>, T, SortedSet<T>, SortedSet<T>.Enumerator>
{
    public override int? CountOf(SortedSet<T> collection) => collection.Count;
    public override SortedSet<T>.Enumerator GetEnumerator(SortedSet<T> collection) => collection.GetEnumerator();
    public override SortedSet<T> Create(int capacity) => [];
    public override void Add(ref SortedSet<T> builder, T element) => builder.Add(element);
    public override SortedSet<T> Complete(SortedSet<T> builder) => builder;
}

internal sealed class LinkedListShape<T> : SequenceShape<LinkedList<T>, T, LinkedList<T>, LinkedList<T>.Enumerator>
{
    public override int? CountOf(LinkedList<T> collection) => collection.Count;
    public override LinkedList<T>.Enumerator GetEnumerator(LinkedList<T> collection) => collection.GetEnumerator();
    public override LinkedList<T> Create(int capacity) => new();
    public override void Add(ref LinkedList<T> builder, T element) => builder.AddLast(element);
    public override LinkedList<T> Complete(LinkedList<T> builder) => builder;
}

internal sealed class ObservableCollectionShape<T>
    : SequenceShape<ObservableCollection<T>, T, ObservableCollection<T>, IEnumerator<T>>
{
    public override int? CountOf(ObservableCollection<T> collection) => collection.Count;
    public override IEnumerator<T> GetEnumerator(ObservableCollection<T> collection) => collection.GetEnumerator();
    public override ObservableCollection<T> Create(int capacity) => [];
    public override void Add(ref ObservableCollection<T> builder, T element) => builder.Add(element);
    public override ObservableCollection<T> Complete(ObservableCollection<T> builder) => builder;
}

/// <summary>A stack enumerates from the top, so its elements are written bottom-up and pushed back in that order.</summary>
internal sealed class StackShape<T> : SequenceShape<Stack<T>, T, Stack<T>, Stack<T>.Enumerator>
{
    public override bool ReverseOnWrite => true;
    public override int? CountOf(Stack<T> collection) => collection.Count;
    public override Stack<T>.Enumerator GetEnumerator(Stack<T> collection) => collection.GetEnumerator();
    public override Stack<T> Create(int capacity) => new(capacity);
    public override void Add(ref Stack<T> builder, T element) => builder.Push(element);
    public override Stack<T> Complete(Stack<T> builder) => builder;
}

internal sealed class QueueShape<T> : SequenceShape<Queue<T>, T, Queue<T>, Queue<T>.Enumerator>
{
    public override int? CountOf(Queue<T> collection) => collection.Count;
    public override Queue<T>.Enumerator GetEnumerator(Queue<T> collection) => collection.GetEnumerator();
    public override Queue<T> Create(int capacity) => new(capacity);
    public override void Add(ref Queue<T> builder, T element) => builder.Enqueue(element);
    public override Queue<T> Complete(Queue<T> builder) => builder;
}

internal sealed class ConcurrentBagShape<T> : SequenceShape<ConcurrentBag<T>, T, ConcurrentBag<T>, IEnumerator<T>>
{
    public override int? CountOf(ConcurrentBag<T> collection) => collection.Count;
    public override IEnumerator<T> GetEnumerator(ConcurrentBag<T> collection) => collection.GetEnumerator();
    public override ConcurrentBag<T> Create(int capacity) => [];
    public override void Add(ref ConcurrentBag<T> builder, T element) => builder.Add(element);
    public override ConcurrentBag<T> Complete(ConcurrentBag<T> builder) => builder;
}

internal sealed class ConcurrentQueueShape<T> : SequenceShape<ConcurrentQueue<T>, T, ConcurrentQueue<T>, IEnumerator<T>>
{
    public override int? CountOf(ConcurrentQueue<T> collection) => collection.Count;
    public override IEnumerator<T> GetEnumerator(ConcurrentQueue<T> collection) => collection.GetEnumerator();
    public override ConcurrentQueue<T> Create(int capacity) => new();
    public override void Add(ref ConcurrentQueue<T> builder, T element) => builder.Enqueue(element);
    public override ConcurrentQueue<T> Complete(ConcurrentQueue<T> builder) => builder;
}

internal sealed class ConcurrentStackShape<T> : SequenceShape<ConcurrentStack<T>, T, ConcurrentStack<T>, IEnumerator<T>>
{
    public override bool ReverseOnWrite => true;
    public override int? CountOf(ConcurrentStack<T> collection) => collection.Count;
    public override IEnumerator<T> GetEnumerator(ConcurrentStack<T> collection) => collection.GetEnumerator();
    public override ConcurrentStack<T> Create(int capacity) => new();
    public override void Add(ref ConcurrentStack<T> builder, T element) => builder.Push(element);
    public override ConcurrentStack<T> Complete(ConcurrentStack<T> builder) => builder;
}

internal sealed class ReadOnlyObservableCollectionShape<T>
    : SequenceShape<ReadOnlyObservableCollection<T>, T, ObservableCollection<T>, IEnumerator<T>>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ReadOnlyObservableCollection<T> collection) => collection.Count;
    public override IEnumerator<T> GetEnumerator(ReadOnlyObservableCollection<T> collection) => collection.GetEnumerator();
    public override ObservableCollection<T> Create(int capacity) => [];
    public override void Add(ref ObservableCollection<T> builder, T element) => builder.Add(element);
    public override ReadOnlyObservableCollection<T> Complete(ObservableCollection<T> builder) => new(builder);
}

internal sealed class ReadOnlyCollectionShape<T> : SequenceShape<ReadOnlyCollection<T>, T, List<T>, IEnumerator<T>>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ReadOnlyCollection<T> collection) => collection.Count;
    public override IEnumerator<T> GetEnumerator(ReadOnlyCollection<T> collection) => collection.GetEnumerator();
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override ReadOnlyCollection<T> Complete(List<T> builder) => new(builder);
}

/// <summary>
/// <c>ReadOnlySequence&lt;T&gt;</c> travels as its elements alone, segment after segment, and is
/// read back as a single segment.
/// </summary>
internal sealed class ReadOnlySequenceShape<T>
    : SequenceShape<ReadOnlySequence<T>, T, List<T>, ReadOnlySequenceShape<T>.Enumerator>
{
    public override string CountName => "ReadOnlySequence length";
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ReadOnlySequence<T> collection) =>
        collection.Length <= int.MaxValue ? (int)collection.Length : null;
    public override Enumerator GetEnumerator(ReadOnlySequence<T> collection) => new(collection);
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override ReadOnlySequence<T> Complete(List<T> builder) => new(builder.ToArray());

    /// <summary>The elements of a sequence, segment after segment.</summary>
    internal struct Enumerator(ReadOnlySequence<T> sequence) : IEnumerator<T>
    {
        private SequencePosition _next = sequence.Start;
        private ReadOnlyMemory<T> _segment;
        private int _index = -1;

        public readonly T Current => _segment.Span[_index];

        readonly object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (++_index >= _segment.Length)
            {
                if (!sequence.TryGet(ref _next, out _segment))
                    return false;

                _index = -1;
            }

            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public readonly void Dispose()
        {
        }
    }
}

/// <summary>
/// Any concrete type implementing <c>ICollection&lt;T&gt;</c> with a public parameterless constructor
/// and a public <c>Add</c> method, built in place through that method.
/// </summary>
internal sealed class CustomCollectionShape<TCollection, T> : SequenceShape<TCollection, T, TCollection, IEnumerator<T>>
    where TCollection : ICollection<T>
{
    private readonly Func<TCollection> _create =
        Expression.Lambda<Func<TCollection>>(Expression.New(typeof(TCollection))).Compile();

    private readonly Action<TCollection, T>? _add = PublicAdd();

    public override int? CountOf(TCollection collection) => Counts.Of(collection);
    public override IEnumerator<T> GetEnumerator(TCollection collection) => collection.GetEnumerator();
    public override TCollection Create(int capacity) => _create();

    public override void Add(ref TCollection builder, T element)
    {
        if (_add is null)
            throw new BinaryTypeException(
                $"'{typeof(TCollection)}' has no public instance method Add({typeof(T).Name}).");

        _add(builder, element);
    }

    public override TCollection Complete(TCollection builder) => builder;

    private static Action<TCollection, T>? PublicAdd()
    {
        var method = typeof(TCollection).GetMethod(
            "Add", BindingFlags.Public | BindingFlags.Instance, [typeof(T)]);

        if (method is null)
            return null;

        var collection = Expression.Parameter(typeof(TCollection), "collection");
        var element = Expression.Parameter(typeof(T), "element");
        return Expression.Lambda<Action<TCollection, T>>(
            Expression.Call(collection, method, element), collection, element).Compile();
    }
}
