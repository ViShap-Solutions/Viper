using System.Collections.Frozen;
using System.Collections.Immutable;

namespace ViShap.Viper.Formatters;

internal sealed class ImmutableListShape<T>
    : SequenceShape<ImmutableList<T>, T, ImmutableList<T>.Builder, ImmutableList<T>.Enumerator>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ImmutableList<T> collection) => collection.Count;
    public override ImmutableList<T>.Enumerator GetEnumerator(ImmutableList<T> collection) => collection.GetEnumerator();
    public override ImmutableList<T>.Builder Create(int capacity) => ImmutableList.CreateBuilder<T>();
    public override void Add(ref ImmutableList<T>.Builder builder, T element) => builder.Add(element);
    public override ImmutableList<T> Complete(ImmutableList<T>.Builder builder) => builder.ToImmutable();
}

internal sealed class ImmutableListInterfaceShape<T>
    : SequenceShape<IImmutableList<T>, T, ImmutableList<T>.Builder, IEnumerator<T>>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(IImmutableList<T> collection) => Counts.Of(collection);
    public override IEnumerator<T> GetEnumerator(IImmutableList<T> collection) => collection.GetEnumerator();
    public override ImmutableList<T>.Builder Create(int capacity) => ImmutableList.CreateBuilder<T>();
    public override void Add(ref ImmutableList<T>.Builder builder, T element) => builder.Add(element);
    public override IImmutableList<T> Complete(ImmutableList<T>.Builder builder) => builder.ToImmutable();
}

internal sealed class ImmutableHashSetShape<T>
    : SequenceShape<ImmutableHashSet<T>, T, ImmutableHashSet<T>.Builder, ImmutableHashSet<T>.Enumerator>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ImmutableHashSet<T> collection) => collection.Count;
    public override ImmutableHashSet<T>.Enumerator GetEnumerator(ImmutableHashSet<T> collection) => collection.GetEnumerator();
    public override ImmutableHashSet<T>.Builder Create(int capacity) => ImmutableHashSet.CreateBuilder<T>();
    public override void Add(ref ImmutableHashSet<T>.Builder builder, T element) => builder.Add(element);
    public override ImmutableHashSet<T> Complete(ImmutableHashSet<T>.Builder builder) => builder.ToImmutable();
}

internal sealed class ImmutableSetInterfaceShape<T>
    : SequenceShape<IImmutableSet<T>, T, ImmutableHashSet<T>.Builder, IEnumerator<T>>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(IImmutableSet<T> collection) => Counts.Of(collection);
    public override IEnumerator<T> GetEnumerator(IImmutableSet<T> collection) => collection.GetEnumerator();
    public override ImmutableHashSet<T>.Builder Create(int capacity) => ImmutableHashSet.CreateBuilder<T>();
    public override void Add(ref ImmutableHashSet<T>.Builder builder, T element) => builder.Add(element);
    public override IImmutableSet<T> Complete(ImmutableHashSet<T>.Builder builder) => builder.ToImmutable();
}

internal sealed class ImmutableSortedSetShape<T>
    : SequenceShape<ImmutableSortedSet<T>, T, ImmutableSortedSet<T>.Builder, ImmutableSortedSet<T>.Enumerator>
{
    public override bool BuilderIsInstance => false;
    public override int? CountOf(ImmutableSortedSet<T> collection) => collection.Count;
    public override ImmutableSortedSet<T>.Enumerator GetEnumerator(ImmutableSortedSet<T> collection) => collection.GetEnumerator();
    public override ImmutableSortedSet<T>.Builder Create(int capacity) => ImmutableSortedSet.CreateBuilder<T>();
    public override void Add(ref ImmutableSortedSet<T>.Builder builder, T element) => builder.Add(element);
    public override ImmutableSortedSet<T> Complete(ImmutableSortedSet<T>.Builder builder) => builder.ToImmutable();
}

/// <summary><c>ImmutableQueue&lt;T&gt;</c> and <c>IImmutableQueue&lt;T&gt;</c>, which have no O(1) count.</summary>
internal sealed class ImmutableQueueShape<TCollection, T> : SequenceShape<TCollection, T, List<T>, IEnumerator<T>>
    where TCollection : class, IImmutableQueue<T>
{
    public override string CountName => "ImmutableQueue count";
    public override bool BuilderIsInstance => false;
    public override int? CountOf(TCollection collection) => null;
    public override IEnumerator<T> GetEnumerator(TCollection collection) => collection.GetEnumerator();
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override TCollection Complete(List<T> builder) => (TCollection)(object)ImmutableQueue.CreateRange(builder);
}

/// <summary>
/// <c>ImmutableStack&lt;T&gt;</c> and <c>IImmutableStack&lt;T&gt;</c>, which have no O(1) count and
/// enumerate from the top, so the elements are written bottom-up.
/// </summary>
internal sealed class ImmutableStackShape<TCollection, T> : SequenceShape<TCollection, T, List<T>, IEnumerator<T>>
    where TCollection : class, IImmutableStack<T>
{
    public override string CountName => "ImmutableStack count";
    public override bool ReverseOnWrite => true;
    public override bool BuilderIsInstance => false;
    public override int? CountOf(TCollection collection) => null;
    public override IEnumerator<T> GetEnumerator(TCollection collection) => collection.GetEnumerator();
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override TCollection Complete(List<T> builder) => (TCollection)(object)ImmutableStack.CreateRange(builder);
}

internal sealed class FrozenSetShape<T> : SequenceShape<FrozenSet<T>, T, List<T>, FrozenSet<T>.Enumerator>
{
    public override string CountName => "FrozenSet count";
    public override bool BuilderIsInstance => false;
    public override int? CountOf(FrozenSet<T> collection) => collection.Count;
    public override FrozenSet<T>.Enumerator GetEnumerator(FrozenSet<T> collection) => collection.GetEnumerator();
    public override List<T> Create(int capacity) => new(capacity);
    public override void Add(ref List<T> builder, T element) => builder.Add(element);
    public override FrozenSet<T> Complete(List<T> builder) => builder.ToFrozenSet();
}
