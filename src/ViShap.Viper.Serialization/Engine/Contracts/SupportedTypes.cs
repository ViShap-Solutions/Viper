using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace ViShap.Viper.Contracts;

/// <summary>
/// The supported types of the format, one method per entry of the table of supported types, each
/// closing its entry over the types of an application: <c>SupportedTypes.List&lt;Line&gt;()</c> is
/// <c>List&lt;Line&gt;</c>. A <see cref="BinarySerializerContext"/> adds the result for every type it
/// reaches, which is what the source generator writes into a context's constructor; the serializer then
/// encodes those types without building anything for them by reflection, which an application
/// published trimmed or with native AOT requires.
/// </summary>
/// <remarks>
/// The scalars — the primitives, <c>string</c>, the time and numeric types, <c>Guid</c>, <c>Uri</c>,
/// <c>Version</c>, <c>StringBuilder</c>, <c>CultureInfo</c>, <c>BitArray</c> — are always supported
/// and have no method here. Every method creates the encoding the serializer would build for the same
/// type by reflection, so adding one never changes a byte the serializer writes.
/// </remarks>
public static class SupportedTypes
{
    // --- Members, enums, nullables and arrays ---------------------------------------------------------

    /// <summary>A member-encoded type, encoded through its <see cref="TypeContract{T}"/>.</summary>
    /// <typeparam name="T">A class or struct that no other entry of the table claims.</typeparam>
    /// <exception cref="BinaryConfigurationException">
    /// <typeparamref name="T"/> is claimed by another entry — a scalar, a collection, an array, a
    /// supported generic type — or is a delegate; or it declares <see cref="BinaryUnionAttribute"/> arms, which
    /// <see cref="Union{T}"/> takes.
    /// </exception>
    public static SupportedType Object<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
    {
        Require<T>(TypeRule.Object, nameof(Object));

        if (typeof(T).IsDefined(typeof(BinaryUnionAttribute), inherit: false))
            throw new BinaryConfigurationException(
                $"'{typeof(T)}' declares [BinaryUnion] arms — use SupportedTypes.Union with the same arms.");

        return Of(new ObjectCodec<T>(union: null));
    }

    /// <summary>
    /// A member-encoded base class or interface whose values are any of the given types, each written
    /// with its tag, as <see cref="BinaryUnionAttribute"/> declares them.
    /// </summary>
    /// <typeparam name="T">The declared base type.</typeparam>
    /// <param name="arms">The tag and the runtime type of each arm.</param>
    /// <exception cref="BinaryConfigurationException">
    /// <typeparamref name="T"/> is claimed by another entry of the table; or a tag is outside 0–255 or
    /// repeated; or a type is repeated, <see langword="null"/> or not assignable to <typeparamref name="T"/>.
    /// </exception>
    public static SupportedType Union<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        params (int Tag, Type Type)[] arms)
    {
        ArgumentNullException.ThrowIfNull(arms);
        Require<T>(TypeRule.Object, nameof(Union));

        var tagByType = new Dictionary<Type, byte>();
        var typeByTag = new Dictionary<byte, Type>();

        foreach (var (tag, type) in arms)
        {
            if (type is null)
                throw new BinaryConfigurationException($"A union arm of '{typeof(T)}' names no type.");

            if (tag is < 0 or > byte.MaxValue)
                throw new BinaryConfigurationException(
                    $"Union tag {tag} of '{typeof(T)}' must fit in a byte (0-255).");

            if (!typeof(T).IsAssignableFrom(type))
                throw new BinaryConfigurationException(
                    $"Union arm '{type}' is not assignable to '{typeof(T)}'.");

            if (!typeByTag.TryAdd((byte)tag, type))
                throw new BinaryConfigurationException($"'{typeof(T)}' names union tag {tag} more than once.");

            if (!tagByType.TryAdd(type, (byte)tag))
                throw new BinaryConfigurationException($"'{typeof(T)}' names union arm '{type}' more than once.");
        }

        return Of(new ObjectCodec<T>(new UnionMap(tagByType, typeByTag)));
    }

    /// <summary>An enum, encoded as its underlying integer.</summary>
    public static SupportedType Enum<T>()
        where T : struct, Enum => Of(new ScalarCodec<T>(new EnumFormatter<T>()));

    /// <summary><see cref="System.Nullable{T}"/>: a null flag, then the value.</summary>
    public static SupportedType Nullable<T>()
        where T : struct => Of(new NullableCodec<T>());

    /// <summary>A one-dimensional, zero-based array.</summary>
    public static SupportedType Array<T>() => Array(new ArrayView<T>());

    /// <summary>An array of rank 2 or more: its lengths, then its elements in row-major order.</summary>
    /// <typeparam name="TArray">The array type, such as <c>int[,]</c>.</typeparam>
    /// <typeparam name="TElement">Its element type.</typeparam>
    /// <exception cref="BinaryConfigurationException">
    /// <typeparamref name="TArray"/> is not an array of <typeparamref name="TElement"/> of rank 2 or more.
    /// </exception>
    public static SupportedType MultiDimensionalArray<TArray, TElement>()
        where TArray : class
    {
        if (!typeof(TArray).IsArray || typeof(TArray).GetArrayRank() < 2 || typeof(TArray).GetElementType() != typeof(TElement))
            throw new BinaryConfigurationException(
                $"'{typeof(TArray)}' is not an array of '{typeof(TElement)}' of rank 2 or more.");

        return Composite(new MultiDimensionalArrayFormatter<TArray, TElement>());
    }

    /// <summary>
    /// A concrete collection the table names no entry for: built with <paramref name="create"/> and
    /// filled with <paramref name="add"/>, its public instance <c>Add</c>.
    /// </summary>
    /// <typeparam name="TCollection">The collection type: concrete, with a public parameterless constructor.</typeparam>
    /// <typeparam name="T">The element type of the <see cref="ICollection{T}"/> it implements.</typeparam>
    /// <param name="create">Creates an empty collection — <c>() =&gt; new TCollection()</c>.</param>
    /// <param name="add">
    /// Adds one element through the collection's public instance <c>Add(T)</c>, or
    /// <see langword="null"/> when it has none, in which case reading one is refused.
    /// </param>
    /// <exception cref="BinaryConfigurationException">
    /// Another entry of the table claims <typeparamref name="TCollection"/>, or its element type is not
    /// <typeparamref name="T"/>.
    /// </exception>
    public static SupportedType Collection<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TCollection,
        T>(Func<TCollection> create, Action<TCollection, T>? add)
        where TCollection : ICollection<T>
    {
        ArgumentNullException.ThrowIfNull(create);
        Require<TCollection>(TypeRule.Collection, nameof(Collection));

        if (TypeRules.CollectionElement(typeof(TCollection)) != typeof(T))
            throw new BinaryConfigurationException(
                $"'{typeof(TCollection)}' is a collection of '{TypeRules.CollectionElement(typeof(TCollection))}', " +
                $"not of '{typeof(T)}'.");

        return Sequence(new CustomCollectionShape<TCollection, T>(create, add));
    }

    // --- Composites ---------------------------------------------------------------------------------

    /// <summary><see cref="KeyValuePair{TKey, TValue}"/>.</summary>
    public static SupportedType KeyValuePair<TKey, TValue>() => Composite(new KeyValuePairFormatter<TKey, TValue>());

    /// <summary><see cref="System.Lazy{T}"/>, which travels as its value.</summary>
    public static SupportedType Lazy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>() =>
        Composite(new LazyFormatter<T>());

    /// <summary><see cref="System.Tuple{T1}"/>.</summary>
    public static SupportedType Tuple<T1>() => Composite(new TupleFormatter<T1>());

    /// <summary><see cref="System.Tuple{T1, T2}"/>.</summary>
    public static SupportedType Tuple<T1, T2>() => Composite(new TupleFormatter<T1, T2>());

    /// <summary><see cref="System.Tuple{T1, T2, T3}"/>.</summary>
    public static SupportedType Tuple<T1, T2, T3>() => Composite(new TupleFormatter<T1, T2, T3>());

    /// <summary><see cref="System.Tuple{T1, T2, T3, T4}"/>.</summary>
    public static SupportedType Tuple<T1, T2, T3, T4>() => Composite(new TupleFormatter<T1, T2, T3, T4>());

    /// <summary><see cref="System.Tuple{T1, T2, T3, T4, T5}"/>.</summary>
    public static SupportedType Tuple<T1, T2, T3, T4, T5>() => Composite(new TupleFormatter<T1, T2, T3, T4, T5>());

    /// <summary><see cref="System.Tuple{T1, T2, T3, T4, T5, T6}"/>.</summary>
    public static SupportedType Tuple<T1, T2, T3, T4, T5, T6>() => Composite(new TupleFormatter<T1, T2, T3, T4, T5, T6>());

    /// <summary><see cref="System.Tuple{T1, T2, T3, T4, T5, T6, T7}"/>.</summary>
    public static SupportedType Tuple<T1, T2, T3, T4, T5, T6, T7>() =>
        Composite(new TupleFormatter<T1, T2, T3, T4, T5, T6, T7>());

    /// <summary><see cref="System.Tuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/>.</summary>
    public static SupportedType Tuple<T1, T2, T3, T4, T5, T6, T7, TRest>()
        where TRest : notnull => Composite(new TupleFormatter<T1, T2, T3, T4, T5, T6, T7, TRest>());

    /// <summary><see cref="System.ValueTuple{T1}"/>.</summary>
    public static SupportedType ValueTuple<T1>() => Composite(new ValueTupleFormatter<T1>());

    /// <summary><see cref="System.ValueTuple{T1, T2}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2>() => Composite(new ValueTupleFormatter<T1, T2>());

    /// <summary><see cref="System.ValueTuple{T1, T2, T3}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2, T3>() => Composite(new ValueTupleFormatter<T1, T2, T3>());

    /// <summary><see cref="System.ValueTuple{T1, T2, T3, T4}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2, T3, T4>() => Composite(new ValueTupleFormatter<T1, T2, T3, T4>());

    /// <summary><see cref="System.ValueTuple{T1, T2, T3, T4, T5}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2, T3, T4, T5>() =>
        Composite(new ValueTupleFormatter<T1, T2, T3, T4, T5>());

    /// <summary><see cref="System.ValueTuple{T1, T2, T3, T4, T5, T6}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2, T3, T4, T5, T6>() =>
        Composite(new ValueTupleFormatter<T1, T2, T3, T4, T5, T6>());

    /// <summary><see cref="System.ValueTuple{T1, T2, T3, T4, T5, T6, T7}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2, T3, T4, T5, T6, T7>() =>
        Composite(new ValueTupleFormatter<T1, T2, T3, T4, T5, T6, T7>());

    /// <summary><see cref="System.ValueTuple{T1, T2, T3, T4, T5, T6, T7, TRest}"/>.</summary>
    public static SupportedType ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>()
        where TRest : struct => Composite(new ValueTupleFormatter<T1, T2, T3, T4, T5, T6, T7, TRest>());

    // --- Arrays and memory --------------------------------------------------------------------------

    /// <summary><see cref="System.Collections.Immutable.ImmutableArray{T}"/>.</summary>
    public static SupportedType ImmutableArray<T>() => Of(new ImmutableArrayCodec<T>());

    /// <summary><see cref="System.Memory{T}"/>.</summary>
    public static SupportedType Memory<T>() => Array(new MemoryView<T>());

    /// <summary><see cref="System.ReadOnlyMemory{T}"/>.</summary>
    public static SupportedType ReadOnlyMemory<T>() => Array(new ReadOnlyMemoryView<T>());

    /// <summary><see cref="System.ArraySegment{T}"/>.</summary>
    public static SupportedType ArraySegment<T>() => Array(new ArraySegmentView<T>());

    /// <summary><see cref="System.Buffers.ReadOnlySequence{T}"/>.</summary>
    public static SupportedType ReadOnlySequence<T>() => Sequence(new ReadOnlySequenceShape<T>());

    // --- Collections --------------------------------------------------------------------------------

    /// <summary><see cref="System.Collections.Generic.List{T}"/>.</summary>
    public static SupportedType List<T>() => Sequence(new ListShape<T>());

    /// <summary><see cref="System.Collections.Generic.IList{T}"/>, read as a <c>List&lt;T&gt;</c>.</summary>
    public static SupportedType IList<T>() => Sequence(new ListInterfaceShape<IList<T>, T>());

    /// <summary><see cref="System.Collections.Generic.ICollection{T}"/>, read as a <c>List&lt;T&gt;</c>.</summary>
    public static SupportedType ICollection<T>() => Sequence(new ListInterfaceShape<ICollection<T>, T>());

    /// <summary><see cref="System.Collections.Generic.IEnumerable{T}"/>, read as a <c>List&lt;T&gt;</c>.</summary>
    public static SupportedType IEnumerable<T>() => Sequence(new ListInterfaceShape<IEnumerable<T>, T>());

    /// <summary><see cref="System.Collections.Generic.IReadOnlyList{T}"/>, read as a <c>List&lt;T&gt;</c>.</summary>
    public static SupportedType IReadOnlyList<T>() => Sequence(new ListInterfaceShape<IReadOnlyList<T>, T>());

    /// <summary><see cref="System.Collections.Generic.IReadOnlyCollection{T}"/>, read as a <c>List&lt;T&gt;</c>.</summary>
    public static SupportedType IReadOnlyCollection<T>() =>
        Sequence(new ListInterfaceShape<IReadOnlyCollection<T>, T>());

    /// <summary><see cref="System.Collections.Generic.HashSet{T}"/>.</summary>
    public static SupportedType HashSet<T>() => Sequence(new HashSetShape<T>());

    /// <summary><see cref="System.Collections.Generic.ISet{T}"/>, read as a <c>HashSet&lt;T&gt;</c>.</summary>
    public static SupportedType ISet<T>() => Sequence(new SetInterfaceShape<T>());

    /// <summary><see cref="System.Collections.Generic.SortedSet{T}"/>.</summary>
    public static SupportedType SortedSet<T>() => Sequence(new SortedSetShape<T>());

    /// <summary><see cref="System.Collections.Generic.LinkedList{T}"/>.</summary>
    public static SupportedType LinkedList<T>() => Sequence(new LinkedListShape<T>());

    /// <summary><see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>.</summary>
    public static SupportedType ObservableCollection<T>() => Sequence(new ObservableCollectionShape<T>());

    /// <summary><see cref="System.Collections.Generic.Stack{T}"/>.</summary>
    public static SupportedType Stack<T>() => Sequence(new StackShape<T>());

    /// <summary><see cref="System.Collections.Generic.Queue{T}"/>.</summary>
    public static SupportedType Queue<T>() => Sequence(new QueueShape<T>());

    /// <summary><see cref="System.Collections.Concurrent.ConcurrentBag{T}"/>.</summary>
    public static SupportedType ConcurrentBag<T>() => Sequence(new ConcurrentBagShape<T>());

    /// <summary><see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>.</summary>
    public static SupportedType ConcurrentQueue<T>() => Sequence(new ConcurrentQueueShape<T>());

    /// <summary><see cref="System.Collections.Concurrent.ConcurrentStack{T}"/>.</summary>
    public static SupportedType ConcurrentStack<T>() => Sequence(new ConcurrentStackShape<T>());

    /// <summary><see cref="System.Collections.ObjectModel.ReadOnlyObservableCollection{T}"/>.</summary>
    public static SupportedType ReadOnlyObservableCollection<T>() => Sequence(new ReadOnlyObservableCollectionShape<T>());

    /// <summary><see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/>.</summary>
    public static SupportedType ReadOnlyCollection<T>() => Sequence(new ReadOnlyCollectionShape<T>());

    // --- Immutable and frozen collections -----------------------------------------------------------

    /// <summary><see cref="System.Collections.Immutable.ImmutableList{T}"/>.</summary>
    public static SupportedType ImmutableList<T>() => Sequence(new ImmutableListShape<T>());

    /// <summary><see cref="System.Collections.Immutable.IImmutableList{T}"/>.</summary>
    public static SupportedType IImmutableList<T>() => Sequence(new ImmutableListInterfaceShape<T>());

    /// <summary><see cref="System.Collections.Immutable.ImmutableHashSet{T}"/>.</summary>
    public static SupportedType ImmutableHashSet<T>() => Sequence(new ImmutableHashSetShape<T>());

    /// <summary><see cref="System.Collections.Immutable.IImmutableSet{T}"/>.</summary>
    public static SupportedType IImmutableSet<T>() => Sequence(new ImmutableSetInterfaceShape<T>());

    /// <summary><see cref="System.Collections.Immutable.ImmutableSortedSet{T}"/>.</summary>
    public static SupportedType ImmutableSortedSet<T>() => Sequence(new ImmutableSortedSetShape<T>());

    /// <summary><see cref="System.Collections.Immutable.ImmutableQueue{T}"/>.</summary>
    public static SupportedType ImmutableQueue<T>() => Sequence(new ImmutableQueueShape<ImmutableQueue<T>, T>());

    /// <summary><see cref="System.Collections.Immutable.IImmutableQueue{T}"/>.</summary>
    public static SupportedType IImmutableQueue<T>() => Sequence(new ImmutableQueueShape<IImmutableQueue<T>, T>());

    /// <summary><see cref="System.Collections.Immutable.ImmutableStack{T}"/>.</summary>
    public static SupportedType ImmutableStack<T>() => Sequence(new ImmutableStackShape<ImmutableStack<T>, T>());

    /// <summary><see cref="System.Collections.Immutable.IImmutableStack{T}"/>.</summary>
    public static SupportedType IImmutableStack<T>() => Sequence(new ImmutableStackShape<IImmutableStack<T>, T>());

    /// <summary><see cref="System.Collections.Frozen.FrozenSet{T}"/>.</summary>
    public static SupportedType FrozenSet<T>() => Sequence(new FrozenSetShape<T>());

    // --- Maps ---------------------------------------------------------------------------------------

    /// <summary><see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/>.</summary>
    public static SupportedType Dictionary<TKey, TValue>()
        where TKey : notnull => Map(new DictionaryShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Generic.IDictionary{TKey, TValue}"/>, read as a <c>Dictionary&lt;TKey, TValue&gt;</c>.</summary>
    public static SupportedType IDictionary<TKey, TValue>()
        where TKey : notnull => Map(new DictionaryInterfaceShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Generic.IReadOnlyDictionary{TKey, TValue}"/>, read as a <c>Dictionary&lt;TKey, TValue&gt;</c>.</summary>
    public static SupportedType IReadOnlyDictionary<TKey, TValue>()
        where TKey : notnull => Map(new ReadOnlyDictionaryShape<IReadOnlyDictionary<TKey, TValue>, TKey, TValue>());

    /// <summary><see cref="System.Collections.ObjectModel.ReadOnlyDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType ReadOnlyDictionary<TKey, TValue>()
        where TKey : notnull => Map(new ReadOnlyDictionaryShape<ReadOnlyDictionary<TKey, TValue>, TKey, TValue>());

    /// <summary><see cref="System.Collections.Generic.SortedDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType SortedDictionary<TKey, TValue>()
        where TKey : notnull => Map(new SortedDictionaryShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Generic.SortedList{TKey, TValue}"/>.</summary>
    public static SupportedType SortedList<TKey, TValue>()
        where TKey : notnull => Map(new SortedListShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Generic.PriorityQueue{TElement, TPriority}"/>.</summary>
    public static SupportedType PriorityQueue<TElement, TPriority>() => Map(new PriorityQueueShape<TElement, TPriority>());

    /// <summary><see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType ConcurrentDictionary<TKey, TValue>()
        where TKey : notnull => Map(new ConcurrentDictionaryShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Immutable.ImmutableDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType ImmutableDictionary<TKey, TValue>()
        where TKey : notnull => Map(new ImmutableDictionaryShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Immutable.IImmutableDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType IImmutableDictionary<TKey, TValue>()
        where TKey : notnull => Map(new ImmutableDictionaryInterfaceShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Immutable.ImmutableSortedDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType ImmutableSortedDictionary<TKey, TValue>()
        where TKey : notnull => Map(new ImmutableSortedDictionaryShape<TKey, TValue>());

    /// <summary><see cref="System.Collections.Frozen.FrozenDictionary{TKey, TValue}"/>.</summary>
    public static SupportedType FrozenDictionary<TKey, TValue>()
        where TKey : notnull => Map(new FrozenDictionaryShape<TKey, TValue>());

    // --- The engine's codec of each shape -----------------------------------------------------------

    private static SupportedType Of<T>(Codec<T> codec) => new(typeof(T), codec, () => OfferedCodec<T>.Offer(codec));

    private static SupportedType Sequence<TCollection, TElement, TBuilder, TEnumerator>(
        ISequenceShape<TCollection, TElement, TBuilder, TEnumerator> shape)
        where TEnumerator : IEnumerator<TElement> =>
        Of(new SequenceCodec<TCollection, TElement, TBuilder, TEnumerator>(shape));

    private static SupportedType Map<TMap, TKey, TValue, TBuilder, TEnumerator>(
        IMapShape<TMap, TKey, TValue, TBuilder, TEnumerator> shape)
        where TEnumerator : IEnumerator<KeyValuePair<TKey, TValue>> =>
        Of(new MapCodec<TMap, TKey, TValue, TBuilder, TEnumerator>(shape));

    private static SupportedType Array<TCollection, TElement>(IArrayShape<TCollection, TElement> shape) =>
        Of(new ArrayCodec<TCollection, TElement>(shape));

    private static SupportedType Composite<T>(ICompositeFormatter<T> formatter) => Of(new CompositeCodec<T>(formatter));

    /// <summary>Refuses a type another entry of the table claims, so an offer is always the registry's own.</summary>
    private static void Require<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        TypeRule rule, string method)
    {
        var actual = TypeRules.Classify(typeof(T));
        if (actual != rule)
            throw new BinaryConfigurationException(
                $"SupportedTypes.{method} cannot describe '{typeof(T)}', which the table of supported types " +
                $"encodes as {Describe(actual)}.");
    }

    private static string Describe(TypeRule rule) => rule switch
    {
        TypeRule.Delegate => "a delegate, which cannot be serialized",
        TypeRule.Nullable => "a nullable value — use SupportedTypes.Nullable",
        TypeRule.Scalar => "a scalar, which is always supported",
        TypeRule.Enum => "an enum — use SupportedTypes.Enum",
        TypeRule.Array => "an array — use SupportedTypes.Array",
        TypeRule.MultiDimensionalArray => "a multi-dimensional array — use SupportedTypes.MultiDimensionalArray",
        TypeRule.UnsupportedArray => "an array with a non-zero lower bound, which has no encoding",
        TypeRule.Definition => "a supported generic type — use the method named after its definition",
        TypeRule.Collection => "a collection — use SupportedTypes.Collection",
        _ => "a member-encoded type — use SupportedTypes.Object or SupportedTypes.Union"
    };
}
