namespace ViShap.Viper.Formatters;

/// <summary>
/// A self-contained value with no children and no data-driven allocation: primitives, times, GUIDs,
/// strings, blobs. Everything it can read is bounded by a fixed size or by a checked primitive, and
/// it never sees a limit, a budget or a stream — only the checked primitives of
/// <see cref="WireReader"/> and <see cref="WireWriter"/>.
/// <para>
/// A scalar reference type begins with a length or a count — a string's byte length, a bit array's
/// bit count — and that number carries its null: the formatter writes it one higher through the
/// payload string and the folded primitives, and the engine writes a null as its zero.
/// </para>
/// </summary>
internal interface IScalarFormatter<T>
{
    /// <summary>The fewest bytes a value occupies on the wire.</summary>
    int MinimumWireSize { get; }

    void Write(ref WireWriter writer, T value);

    T Read(ref WireReader reader);
}

/// <summary>
/// The shape of a sequence: how to count, enumerate, build and complete one. It receives no count
/// from the wire and no primitive: the engine's codec reads the count, owns the loop over the
/// elements, charges depth, nodes and identity, and hands the shape each element already read.
/// </summary>
/// <typeparam name="TCollection">The declared collection type.</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
/// <typeparam name="TBuilder">
/// What elements are added to while reading: the collection itself for a mutable one, a builder or a
/// list for one that exists only once its elements are known.
/// </typeparam>
/// <typeparam name="TEnumerator">
/// The enumerator the collection yields its elements through, in wire order; a struct enumerator
/// enumerates without allocating.
/// </typeparam>
internal interface ISequenceShape<TCollection, TElement, TBuilder, TEnumerator>
    where TEnumerator : IEnumerator<TElement>
{
    /// <summary>Which limit bounds the count.</summary>
    CountKind CountKind { get; }

    /// <summary>The count's name in limit and format messages, e.g. "Collection count".</summary>
    string CountName { get; }

    /// <summary>Whether the elements go on the wire in reverse of their enumeration order.</summary>
    bool ReverseOnWrite { get; }

    /// <summary>
    /// Whether <see cref="Create"/> returns the final instance, so its identity can be registered
    /// before its elements are read and a cycle through it can close.
    /// </summary>
    bool BuilderIsInstance { get; }

    /// <summary>The element count when the collection knows it; <see langword="null"/> otherwise.</summary>
    int? CountOf(TCollection collection);

    /// <summary>
    /// The elements as one contiguous span when the collection keeps them that way; the engine then
    /// writes them from the span instead of enumerating.
    /// </summary>
    bool TryGetSpan(TCollection collection, out ReadOnlySpan<TElement> elements);

    TEnumerator GetEnumerator(TCollection collection);

    TBuilder Create(int capacity);

    void Add(ref TBuilder builder, TElement element);

    TCollection Complete(TBuilder builder);
}

/// <summary>
/// The shape of a sequence whose elements lie in one contiguous array — an array, a memory, an array
/// segment. The engine writes the elements from the span the shape exposes and reads them into an
/// array of their final length, which the shape wraps.
/// </summary>
internal interface IArrayShape<TCollection, TElement>
{
    CountKind CountKind { get; }

    string CountName { get; }

    ReadOnlySpan<TElement> Elements(TCollection collection);

    TCollection Wrap(TElement[] elements);
}

/// <summary>The shape of a map: the same division of labour as a sequence, for key/value entries.</summary>
internal interface IMapShape<TMap, TKey, TValue, TBuilder, TEnumerator>
    where TEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>
{
    CountKind CountKind { get; }

    string CountName { get; }

    bool BuilderIsInstance { get; }

    int? CountOf(TMap map);

    TEnumerator GetEnumerator(TMap map);

    TBuilder Create(int capacity);

    void Add(ref TBuilder builder, TKey key, TValue value);

    TMap Complete(TBuilder builder);
}

/// <summary>
/// A value with a fixed, type-determined child layout — tuples, key/value pairs, lazy values — or an
/// irregular one — the rank of a multi-dimensional array. The engine has already charged the depth
/// scope, the node budget and identity before calling.
/// <para>
/// The formatter receives a <see cref="CompositeReader"/> or <see cref="CompositeWriter"/>, not the
/// engine and not the payload primitives. Neither surface offers a raw integer, so an array shape
/// exists only as a validated <see cref="ArrayShape"/>, and the elements behind it are read by the
/// engine's own loop.
/// </para>
/// </summary>
internal interface ICompositeFormatter<T>
{
    /// <summary>
    /// Whether the value begins with an array shape, whose rank is then the first number of the value
    /// and carries its null. A composite that begins with a child value carries its null in a flag.
    /// </summary>
    bool BeginsWithShape => false;

    void Write(ref CompositeWriter writer, T value);

    T Read(ref CompositeReader reader);
}
