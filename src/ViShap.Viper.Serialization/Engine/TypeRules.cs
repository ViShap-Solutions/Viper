using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ViShap.Viper.Engine;

/// <summary>The rule of the supported-type table that claims a declared type.</summary>
internal enum TypeRule
{
    Delegate,
    Nullable,
    Scalar,
    Enum,
    Array,
    MultiDimensionalArray,
    UnsupportedArray,
    Definition,
    Collection,
    Object
}

/// <summary>
/// Which rule claims a declared type. The rules apply in order and the first that claims a type wins,
/// so a specific shape always precedes a general one:
/// <list type="number">
/// <item>delegates, which are refused;</item>
/// <item><see cref="Nullable{T}"/>, a null flag around the underlying type's codec;</item>
/// <item>the scalars, by exact type, and enums;</item>
/// <item>arrays;</item>
/// <item>the closed forms of the supported generic definitions — composites, sequences, maps — one
/// shape per definition;</item>
/// <item>any other concrete collection with a public parameterless constructor;</item>
/// <item>everything else, which is member-encoded through its <see cref="TypeContract{T}"/>.</item>
/// </list>
/// The classification inspects the type and builds nothing, so the reflection path and the codecs a
/// context offers are held to the same rules.
/// </summary>
internal static class TypeRules
{
    /// <summary>How a shape is closed over a declared type: over its generic arguments, or over the declared type and then them.</summary>
    public enum Closing
    {
        Arguments,
        DeclaredThenArguments
    }

    /// <summary>The one shape of each supported generic definition.</summary>
    public static readonly FrozenDictionary<Type, (Type Shape, Closing Closing)> Shapes =
        new Dictionary<Type, (Type, Closing)>
        {
            // Composites.
            [typeof(KeyValuePair<,>)] = (typeof(KeyValuePairFormatter<,>), Closing.Arguments),
            [typeof(Lazy<>)] = (typeof(LazyFormatter<>), Closing.Arguments),
            [typeof(Tuple<>)] = (typeof(TupleFormatter<>), Closing.Arguments),
            [typeof(Tuple<,>)] = (typeof(TupleFormatter<,>), Closing.Arguments),
            [typeof(Tuple<,,>)] = (typeof(TupleFormatter<,,>), Closing.Arguments),
            [typeof(Tuple<,,,>)] = (typeof(TupleFormatter<,,,>), Closing.Arguments),
            [typeof(Tuple<,,,,>)] = (typeof(TupleFormatter<,,,,>), Closing.Arguments),
            [typeof(Tuple<,,,,,>)] = (typeof(TupleFormatter<,,,,,>), Closing.Arguments),
            [typeof(Tuple<,,,,,,>)] = (typeof(TupleFormatter<,,,,,,>), Closing.Arguments),
            [typeof(Tuple<,,,,,,,>)] = (typeof(TupleFormatter<,,,,,,,>), Closing.Arguments),
            [typeof(ValueTuple<>)] = (typeof(ValueTupleFormatter<>), Closing.Arguments),
            [typeof(ValueTuple<,>)] = (typeof(ValueTupleFormatter<,>), Closing.Arguments),
            [typeof(ValueTuple<,,>)] = (typeof(ValueTupleFormatter<,,>), Closing.Arguments),
            [typeof(ValueTuple<,,,>)] = (typeof(ValueTupleFormatter<,,,>), Closing.Arguments),
            [typeof(ValueTuple<,,,,>)] = (typeof(ValueTupleFormatter<,,,,>), Closing.Arguments),
            [typeof(ValueTuple<,,,,,>)] = (typeof(ValueTupleFormatter<,,,,,>), Closing.Arguments),
            [typeof(ValueTuple<,,,,,,>)] = (typeof(ValueTupleFormatter<,,,,,,>), Closing.Arguments),
            [typeof(ValueTuple<,,,,,,,>)] = (typeof(ValueTupleFormatter<,,,,,,,>), Closing.Arguments),

            // Sequences.
            [typeof(ImmutableArray<>)] = (typeof(ImmutableArrayCodec<>), Closing.Arguments),
            [typeof(Memory<>)] = (typeof(MemoryView<>), Closing.Arguments),
            [typeof(ReadOnlyMemory<>)] = (typeof(ReadOnlyMemoryView<>), Closing.Arguments),
            [typeof(ArraySegment<>)] = (typeof(ArraySegmentView<>), Closing.Arguments),
            [typeof(ReadOnlySequence<>)] = (typeof(ReadOnlySequenceShape<>), Closing.Arguments),
            [typeof(List<>)] = (typeof(ListShape<>), Closing.Arguments),
            [typeof(IList<>)] = (typeof(ListInterfaceShape<,>), Closing.DeclaredThenArguments),
            [typeof(ICollection<>)] = (typeof(ListInterfaceShape<,>), Closing.DeclaredThenArguments),
            [typeof(IEnumerable<>)] = (typeof(ListInterfaceShape<,>), Closing.DeclaredThenArguments),
            [typeof(IReadOnlyList<>)] = (typeof(ListInterfaceShape<,>), Closing.DeclaredThenArguments),
            [typeof(IReadOnlyCollection<>)] = (typeof(ListInterfaceShape<,>), Closing.DeclaredThenArguments),
            [typeof(HashSet<>)] = (typeof(HashSetShape<>), Closing.Arguments),
            [typeof(ISet<>)] = (typeof(SetInterfaceShape<>), Closing.Arguments),
            [typeof(SortedSet<>)] = (typeof(SortedSetShape<>), Closing.Arguments),
            [typeof(LinkedList<>)] = (typeof(LinkedListShape<>), Closing.Arguments),
            [typeof(ObservableCollection<>)] = (typeof(ObservableCollectionShape<>), Closing.Arguments),
            [typeof(Stack<>)] = (typeof(StackShape<>), Closing.Arguments),
            [typeof(Queue<>)] = (typeof(QueueShape<>), Closing.Arguments),
            [typeof(ConcurrentBag<>)] = (typeof(ConcurrentBagShape<>), Closing.Arguments),
            [typeof(ConcurrentQueue<>)] = (typeof(ConcurrentQueueShape<>), Closing.Arguments),
            [typeof(ConcurrentStack<>)] = (typeof(ConcurrentStackShape<>), Closing.Arguments),
            [typeof(ReadOnlyObservableCollection<>)] = (typeof(ReadOnlyObservableCollectionShape<>), Closing.Arguments),
            [typeof(ReadOnlyCollection<>)] = (typeof(ReadOnlyCollectionShape<>), Closing.Arguments),
            [typeof(ImmutableList<>)] = (typeof(ImmutableListShape<>), Closing.Arguments),
            [typeof(IImmutableList<>)] = (typeof(ImmutableListInterfaceShape<>), Closing.Arguments),
            [typeof(ImmutableHashSet<>)] = (typeof(ImmutableHashSetShape<>), Closing.Arguments),
            [typeof(IImmutableSet<>)] = (typeof(ImmutableSetInterfaceShape<>), Closing.Arguments),
            [typeof(ImmutableSortedSet<>)] = (typeof(ImmutableSortedSetShape<>), Closing.Arguments),
            [typeof(ImmutableQueue<>)] = (typeof(ImmutableQueueShape<,>), Closing.DeclaredThenArguments),
            [typeof(IImmutableQueue<>)] = (typeof(ImmutableQueueShape<,>), Closing.DeclaredThenArguments),
            [typeof(ImmutableStack<>)] = (typeof(ImmutableStackShape<,>), Closing.DeclaredThenArguments),
            [typeof(IImmutableStack<>)] = (typeof(ImmutableStackShape<,>), Closing.DeclaredThenArguments),
            [typeof(FrozenSet<>)] = (typeof(FrozenSetShape<>), Closing.Arguments),

            // Maps.
            [typeof(ReadOnlyDictionary<,>)] = (typeof(ReadOnlyDictionaryShape<,,>), Closing.DeclaredThenArguments),
            [typeof(IReadOnlyDictionary<,>)] = (typeof(ReadOnlyDictionaryShape<,,>), Closing.DeclaredThenArguments),
            [typeof(FrozenDictionary<,>)] = (typeof(FrozenDictionaryShape<,>), Closing.Arguments),
            [typeof(ImmutableDictionary<,>)] = (typeof(ImmutableDictionaryShape<,>), Closing.Arguments),
            [typeof(IImmutableDictionary<,>)] = (typeof(ImmutableDictionaryInterfaceShape<,>), Closing.Arguments),
            [typeof(ImmutableSortedDictionary<,>)] = (typeof(ImmutableSortedDictionaryShape<,>), Closing.Arguments),
            [typeof(ConcurrentDictionary<,>)] = (typeof(ConcurrentDictionaryShape<,>), Closing.Arguments),
            [typeof(SortedDictionary<,>)] = (typeof(SortedDictionaryShape<,>), Closing.Arguments),
            [typeof(SortedList<,>)] = (typeof(SortedListShape<,>), Closing.Arguments),
            [typeof(PriorityQueue<,>)] = (typeof(PriorityQueueShape<,>), Closing.Arguments),
            [typeof(Dictionary<,>)] = (typeof(DictionaryShape<,>), Closing.Arguments),
            [typeof(IDictionary<,>)] = (typeof(DictionaryInterfaceShape<,>), Closing.Arguments)
        }.ToFrozenDictionary();

    /// <summary>The rule that claims <paramref name="type"/>.</summary>
    public static TypeRule Classify(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type))
            return TypeRule.Delegate;

        if (Nullable.GetUnderlyingType(type) is not null)
            return TypeRule.Nullable;

        if (BuiltInCodecs.Scalars.ContainsKey(type))
            return TypeRule.Scalar;

        if (type.IsEnum)
            return TypeRule.Enum;

        if (type.IsArray)
            return type.IsSZArray ? TypeRule.Array
                : type.GetArrayRank() > 1 ? TypeRule.MultiDimensionalArray
                : TypeRule.UnsupportedArray;

        if (type.IsGenericType && Shapes.ContainsKey(type.GetGenericTypeDefinition()))
            return TypeRule.Definition;

        if (CollectionElement(type) is not null)
            return TypeRule.Collection;

        return TypeRule.Object;
    }

    /// <summary>
    /// The element type of a concrete collection the engine can build: not an interface, not
    /// abstract, implementing <c>ICollection&lt;T&gt;</c>, with a public parameterless constructor.
    /// </summary>
    public static Type? CollectionElement(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type type)
    {
        if (type.IsInterface || type.IsAbstract)
            return null;

        var element = type
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>))
            ?.GetGenericArguments()[0];

        return element is not null &&
               type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is not null
            ? element
            : null;
    }
}
