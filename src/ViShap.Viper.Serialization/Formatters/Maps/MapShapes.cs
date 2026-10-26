using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace ViShap.Viper.Formatters;

/// <summary>
/// Defaults shared by the map shapes: the count is a dictionary entry count, and the map is built in
/// place unless a shape says otherwise.
/// </summary>
internal abstract class MapShape<TMap, TKey, TValue, TBuilder, TEnumerator>
    : IMapShape<TMap, TKey, TValue, TBuilder, TEnumerator>
    where TEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>
{
    public virtual CountKind CountKind => CountKind.Dictionary;

    public virtual string CountName => "Dictionary entry count";

    public virtual bool BuilderIsInstance => true;

    public abstract int? CountOf(TMap map);

    public abstract TEnumerator GetEnumerator(TMap map);

    public abstract TBuilder Create(int capacity);

    public abstract void Add(ref TBuilder builder, TKey key, TValue value);

    public abstract TMap Complete(TBuilder builder);

    /// <summary>
    /// Refuses a null key before a container that would accept one through a non-generic path sees it.
    /// </summary>
    protected static TKey RequireKey(TKey key) =>
        key ?? throw new BinaryFormatException("A dictionary key cannot be null.");
}

internal sealed class DictionaryShape<TKey, TValue>
    : MapShape<Dictionary<TKey, TValue>, TKey, TValue, Dictionary<TKey, TValue>, Dictionary<TKey, TValue>.Enumerator>
    where TKey : notnull
{
    public override int? CountOf(Dictionary<TKey, TValue> map) => map.Count;
    public override Dictionary<TKey, TValue>.Enumerator GetEnumerator(Dictionary<TKey, TValue> map) => map.GetEnumerator();
    public override Dictionary<TKey, TValue> Create(int capacity) => new(capacity);
    public override void Add(ref Dictionary<TKey, TValue> builder, TKey key, TValue value) => builder.Add(key, value);
    public override Dictionary<TKey, TValue> Complete(Dictionary<TKey, TValue> builder) => builder;
}

/// <summary><c>IDictionary&lt;K,V&gt;</c>: any implementation is written, and a <c>Dictionary&lt;K,V&gt;</c> is read.</summary>
internal sealed class DictionaryInterfaceShape<TKey, TValue>
    : MapShape<IDictionary<TKey, TValue>, TKey, TValue, Dictionary<TKey, TValue>, IEnumerator<KeyValuePair<TKey, TValue>>>
    where TKey : notnull
{
    public override int? CountOf(IDictionary<TKey, TValue> map) => Counts.Of(map);
    public override IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator(IDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override Dictionary<TKey, TValue> Create(int capacity) => new(capacity);
    public override void Add(ref Dictionary<TKey, TValue> builder, TKey key, TValue value) => builder.Add(key, value);
    public override IDictionary<TKey, TValue> Complete(Dictionary<TKey, TValue> builder) => builder;
}

internal sealed class SortedDictionaryShape<TKey, TValue>
    : MapShape<SortedDictionary<TKey, TValue>, TKey, TValue, SortedDictionary<TKey, TValue>, SortedDictionary<TKey, TValue>.Enumerator>
    where TKey : notnull
{
    public override int? CountOf(SortedDictionary<TKey, TValue> map) => map.Count;
    public override SortedDictionary<TKey, TValue>.Enumerator GetEnumerator(SortedDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override SortedDictionary<TKey, TValue> Create(int capacity) => [];
    public override void Add(ref SortedDictionary<TKey, TValue> builder, TKey key, TValue value) => builder.Add(key, value);
    public override SortedDictionary<TKey, TValue> Complete(SortedDictionary<TKey, TValue> builder) => builder;
}

internal sealed class SortedListShape<TKey, TValue>
    : MapShape<SortedList<TKey, TValue>, TKey, TValue, SortedList<TKey, TValue>, IEnumerator<KeyValuePair<TKey, TValue>>>
    where TKey : notnull
{
    public override int? CountOf(SortedList<TKey, TValue> map) => map.Count;
    public override IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator(SortedList<TKey, TValue> map) => map.GetEnumerator();
    public override SortedList<TKey, TValue> Create(int capacity) => [];
    public override void Add(ref SortedList<TKey, TValue> builder, TKey key, TValue value) => builder.Add(key, value);
    public override SortedList<TKey, TValue> Complete(SortedList<TKey, TValue> builder) => builder;
}

/// <summary>
/// <c>ConcurrentDictionary&lt;K,V&gt;</c> admits a repeated key silently; the engine's count check
/// catches the entry it dropped.
/// </summary>
internal sealed class ConcurrentDictionaryShape<TKey, TValue>
    : MapShape<ConcurrentDictionary<TKey, TValue>, TKey, TValue, ConcurrentDictionary<TKey, TValue>, IEnumerator<KeyValuePair<TKey, TValue>>>
    where TKey : notnull
{
    public override int? CountOf(ConcurrentDictionary<TKey, TValue> map) => map.Count;
    public override IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator(ConcurrentDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override ConcurrentDictionary<TKey, TValue> Create(int capacity) => new();
    public override void Add(ref ConcurrentDictionary<TKey, TValue> builder, TKey key, TValue value) => builder.TryAdd(key, value);
    public override ConcurrentDictionary<TKey, TValue> Complete(ConcurrentDictionary<TKey, TValue> builder) => builder;
}

/// <summary>
/// <c>ReadOnlyDictionary&lt;K,V&gt;</c> and <c>IReadOnlyDictionary&lt;K,V&gt;</c>: any implementation
/// is written, and a <c>ReadOnlyDictionary&lt;K,V&gt;</c> is read.
/// </summary>
internal sealed class ReadOnlyDictionaryShape<TMap, TKey, TValue>
    : MapShape<TMap, TKey, TValue, Dictionary<TKey, TValue>, IEnumerator<KeyValuePair<TKey, TValue>>>
    where TMap : class, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(TMap map) => Counts.Of(map);
    public override IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator(TMap map) => map.GetEnumerator();
    public override Dictionary<TKey, TValue> Create(int capacity) => new(capacity);
    public override void Add(ref Dictionary<TKey, TValue> builder, TKey key, TValue value) => builder.Add(RequireKey(key), value);
    public override TMap Complete(Dictionary<TKey, TValue> builder) => (TMap)(object)new ReadOnlyDictionary<TKey, TValue>(builder);
}

internal sealed class FrozenDictionaryShape<TKey, TValue>
    : MapShape<FrozenDictionary<TKey, TValue>, TKey, TValue, Dictionary<TKey, TValue>, FrozenDictionary<TKey, TValue>.Enumerator>
    where TKey : notnull
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(FrozenDictionary<TKey, TValue> map) => map.Count;
    public override FrozenDictionary<TKey, TValue>.Enumerator GetEnumerator(FrozenDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override Dictionary<TKey, TValue> Create(int capacity) => new(capacity);
    public override void Add(ref Dictionary<TKey, TValue> builder, TKey key, TValue value) => builder.Add(RequireKey(key), value);
    public override FrozenDictionary<TKey, TValue> Complete(Dictionary<TKey, TValue> builder) => builder.ToFrozenDictionary();
}

internal sealed class ImmutableDictionaryShape<TKey, TValue>
    : MapShape<ImmutableDictionary<TKey, TValue>, TKey, TValue, ImmutableDictionary<TKey, TValue>.Builder, ImmutableDictionary<TKey, TValue>.Enumerator>
    where TKey : notnull
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ImmutableDictionary<TKey, TValue> map) => map.Count;
    public override ImmutableDictionary<TKey, TValue>.Enumerator GetEnumerator(ImmutableDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override ImmutableDictionary<TKey, TValue>.Builder Create(int capacity) => ImmutableDictionary.CreateBuilder<TKey, TValue>();
    public override void Add(ref ImmutableDictionary<TKey, TValue>.Builder builder, TKey key, TValue value) => builder.Add(key, value);
    public override ImmutableDictionary<TKey, TValue> Complete(ImmutableDictionary<TKey, TValue>.Builder builder) => builder.ToImmutable();
}

internal sealed class ImmutableDictionaryInterfaceShape<TKey, TValue>
    : MapShape<IImmutableDictionary<TKey, TValue>, TKey, TValue, ImmutableDictionary<TKey, TValue>.Builder, IEnumerator<KeyValuePair<TKey, TValue>>>
    where TKey : notnull
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(IImmutableDictionary<TKey, TValue> map) => Counts.Of(map);
    public override IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator(IImmutableDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override ImmutableDictionary<TKey, TValue>.Builder Create(int capacity) => ImmutableDictionary.CreateBuilder<TKey, TValue>();
    public override void Add(ref ImmutableDictionary<TKey, TValue>.Builder builder, TKey key, TValue value) => builder.Add(key, value);
    public override IImmutableDictionary<TKey, TValue> Complete(ImmutableDictionary<TKey, TValue>.Builder builder) => builder.ToImmutable();
}

internal sealed class ImmutableSortedDictionaryShape<TKey, TValue>
    : MapShape<ImmutableSortedDictionary<TKey, TValue>, TKey, TValue, ImmutableSortedDictionary<TKey, TValue>.Builder, ImmutableSortedDictionary<TKey, TValue>.Enumerator>
    where TKey : notnull
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ImmutableSortedDictionary<TKey, TValue> map) => map.Count;
    public override ImmutableSortedDictionary<TKey, TValue>.Enumerator GetEnumerator(ImmutableSortedDictionary<TKey, TValue> map) => map.GetEnumerator();
    public override ImmutableSortedDictionary<TKey, TValue>.Builder Create(int capacity) => ImmutableSortedDictionary.CreateBuilder<TKey, TValue>();
    public override void Add(ref ImmutableSortedDictionary<TKey, TValue>.Builder builder, TKey key, TValue value) => builder.Add(key, value);
    public override ImmutableSortedDictionary<TKey, TValue> Complete(ImmutableSortedDictionary<TKey, TValue>.Builder builder) => builder.ToImmutable();
}

/// <summary>
/// A priority queue is a sequence of (element, priority) pairs in no particular order, so it uses the
/// same engine-driven entry loop as a dictionary, and its dequeue order is rebuilt from the priorities.
/// </summary>
internal sealed class PriorityQueueShape<TElement, TPriority>
    : MapShape<PriorityQueue<TElement, TPriority>, TElement, TPriority, PriorityQueue<TElement, TPriority>, PriorityQueueShape<TElement, TPriority>.Enumerator>
{
    public override CountKind CountKind => CountKind.Collection;
    public override string CountName => "PriorityQueue count";
    public override int? CountOf(PriorityQueue<TElement, TPriority> map) => map.Count;
    public override Enumerator GetEnumerator(PriorityQueue<TElement, TPriority> map) => new(map.UnorderedItems.GetEnumerator());
    public override PriorityQueue<TElement, TPriority> Create(int capacity) => new();
    public override void Add(ref PriorityQueue<TElement, TPriority> builder, TElement key, TPriority value) => builder.Enqueue(key, value);
    public override PriorityQueue<TElement, TPriority> Complete(PriorityQueue<TElement, TPriority> builder) => builder;

    /// <summary>The queue's unordered pairs, as entries.</summary>
    internal struct Enumerator(PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.Enumerator items)
        : IEnumerator<KeyValuePair<TElement, TPriority>>
    {
        private PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.Enumerator _items = items;

        public readonly KeyValuePair<TElement, TPriority> Current =>
            new(_items.Current.Element, _items.Current.Priority);

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => _items.MoveNext();

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => _items.Dispose();
    }
}
