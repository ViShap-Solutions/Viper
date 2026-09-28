namespace ViShap.Viper.Formatters;

/// <summary>A one-dimensional array.</summary>
internal sealed class ArrayView<T> : IArrayShape<T[], T>
{
    public CountKind CountKind => CountKind.Array;
    public string CountName => "Array length";
    public ReadOnlySpan<T> Elements(T[] collection) => collection;
    public T[] Wrap(T[] elements) => elements;
}

/// <summary>
/// <c>Memory&lt;T&gt;</c>: its elements alone travel, and a read wraps a fresh array exactly as long
/// as the value.
/// </summary>
internal sealed class MemoryView<T> : IArrayShape<Memory<T>, T>
{
    public CountKind CountKind => CountKind.Collection;
    public string CountName => "Memory element count";
    public ReadOnlySpan<T> Elements(Memory<T> collection) => collection.Span;
    public Memory<T> Wrap(T[] elements) => new(elements);
}

internal sealed class ReadOnlyMemoryView<T> : IArrayShape<ReadOnlyMemory<T>, T>
{
    public CountKind CountKind => CountKind.Collection;
    public string CountName => "Memory element count";
    public ReadOnlySpan<T> Elements(ReadOnlyMemory<T> collection) => collection.Span;
    public ReadOnlyMemory<T> Wrap(T[] elements) => new(elements);
}

/// <summary>
/// <c>ArraySegment&lt;T&gt;</c>. A default segment has no backing array at all and is written as an
/// empty segment; a read returns a segment at offset zero over an array exactly as long as it.
/// </summary>
internal sealed class ArraySegmentView<T> : IArrayShape<ArraySegment<T>, T>
{
    public CountKind CountKind => CountKind.Collection;
    public string CountName => "Memory element count";
    public ReadOnlySpan<T> Elements(ArraySegment<T> collection) =>
        collection.Array is null ? default : collection.AsSpan();
    public ArraySegment<T> Wrap(T[] elements) => new(elements);
}
