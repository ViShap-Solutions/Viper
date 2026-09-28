using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace ViShap.Viper.Formatters;

/// <summary>
/// Resolves the codec of a declared type, once per type, for <see cref="FormatterCache{T}"/>. The
/// rules apply in order and the first that claims a type wins, so a specific shape always precedes a
/// general one:
/// <list type="number">
/// <item>delegates, which are refused;</item>
/// <item><see cref="Nullable{T}"/>, a null flag around the underlying type's codec;</item>
/// <item>the scalars, by exact type, and enums;</item>
/// <item>arrays;</item>
/// <item>the closed forms of the supported generic definitions — composites, sequences, maps — one
/// shape per definition, closed once per type;</item>
/// <item>any other concrete collection with a public parameterless constructor;</item>
/// <item>everything else, which is member-encoded through its <see cref="TypeContract{T}"/>.</item>
/// </list>
/// There is no catch-all formatter that could shadow a more specific one by accident.
/// </summary>
internal static class FormatterRegistry
{
    private static readonly FrozenDictionary<Type, object> Scalars = new Dictionary<Type, object>
    {
        [typeof(bool)] = new BooleanFormatter(),
        [typeof(byte)] = new ByteFormatter(),
        [typeof(sbyte)] = new SByteFormatter(),
        [typeof(short)] = new Int16Formatter(),
        [typeof(ushort)] = new UInt16Formatter(),
        [typeof(int)] = new Int32Formatter(),
        [typeof(uint)] = new UInt32Formatter(),
        [typeof(long)] = new Int64Formatter(),
        [typeof(ulong)] = new UInt64Formatter(),
        [typeof(float)] = new SingleFormatter(),
        [typeof(double)] = new DoubleFormatter(),
        [typeof(decimal)] = new DecimalFormatter(),
        [typeof(char)] = new CharFormatter(),
        [typeof(string)] = new StringFormatter(),
        [typeof(Half)] = new HalfFormatter(),
        [typeof(Int128)] = new Int128Formatter(),
        [typeof(UInt128)] = new UInt128Formatter(),
        [typeof(IntPtr)] = new IntPtrFormatter(),
        [typeof(UIntPtr)] = new UIntPtrFormatter(),
        [typeof(Rune)] = new RuneFormatter(),
        [typeof(BigInteger)] = new BigIntegerFormatter(),

        [typeof(DateTime)] = new DateTimeFormatter(),
        [typeof(DateTimeOffset)] = new DateTimeOffsetFormatter(),
        [typeof(TimeSpan)] = new TimeSpanFormatter(),
        [typeof(DateOnly)] = new DateOnlyFormatter(),
        [typeof(TimeOnly)] = new TimeOnlyFormatter(),
        [typeof(TimeZoneInfo)] = new TimeZoneInfoFormatter(),

        [typeof(Complex)] = new ComplexFormatter(),
        [typeof(Vector2)] = new Vector2Formatter(),
        [typeof(Vector3)] = new Vector3Formatter(),
        [typeof(Vector4)] = new Vector4Formatter(),
        [typeof(Quaternion)] = new QuaternionFormatter(),
        [typeof(Plane)] = new PlaneFormatter(),
        [typeof(Matrix3x2)] = new Matrix3x2Formatter(),
        [typeof(Matrix4x4)] = new Matrix4x4Formatter(),

        [typeof(Guid)] = new GuidFormatter(),
        [typeof(Uri)] = new UriFormatter(),
        [typeof(Version)] = new VersionFormatter(),
        [typeof(StringBuilder)] = new StringBuilderFormatter(),
        [typeof(CultureInfo)] = new CultureInfoFormatter(),
        [typeof(BitArray)] = new BitArrayFormatter()
    }.ToFrozenDictionary();

    /// <summary>How a shape is closed over a declared type: over its generic arguments, or over the declared type and then them.</summary>
    private enum Closing
    {
        Arguments,
        DeclaredThenArguments
    }

    /// <summary>The one shape of each supported generic definition.</summary>
    private static readonly FrozenDictionary<Type, (Type Shape, Closing Closing)> Shapes =
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

    /// <summary>
    /// The codec of <typeparamref name="T"/>. A type the engine cannot build a codec for resolves to
    /// one that raises <see cref="BinaryTypeException"/> wherever the type is used.
    /// </summary>
    public static Codec<T> Resolve<T>()
    {
        try
        {
            return (Codec<T>)Create(typeof(T));
        }
        catch (Exception ex) when (ex is ArgumentException or TargetInvocationException or NotSupportedException
                                      or TypeLoadException or InvalidOperationException)
        {
            return new UnsupportedCodec<T>(
                $"'{typeof(T)}' cannot be serialized: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
        }
    }

    private static object Create(Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type))
            return Instantiate(
                typeof(RejectedCodec<>).MakeGenericType(type),
                $"Delegate types cannot be serialized ('{type}') — as a root value, a member, " +
                "or a collection element. Exclude the containing member with [BinaryIgnore] instead.",
                $"Delegate types cannot be deserialized ('{type}').");

        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Instantiate(typeof(NullableCodec<>).MakeGenericType(underlying));

        if (Scalars.TryGetValue(type, out var scalar))
            return Instantiate(
                (type.IsValueType ? typeof(ScalarCodec<>) : typeof(NullableScalarCodec<>)).MakeGenericType(type),
                scalar);

        if (type.IsEnum)
            return Instantiate(
                typeof(ScalarCodec<>).MakeGenericType(type),
                Instantiate(typeof(EnumFormatter<>).MakeGenericType(type)));

        if (type.IsArray)
            return ForArray(type);

        if (type.IsGenericType && Shapes.TryGetValue(type.GetGenericTypeDefinition(), out var entry))
        {
            var arguments = type.GetGenericArguments();
            var closed = entry.Closing == Closing.Arguments
                ? entry.Shape.MakeGenericType(arguments)
                : entry.Shape.MakeGenericType([type, .. arguments]);

            var shape = Instantiate(closed);
            return closed.IsSubclassOf(typeof(Codec<>).MakeGenericType(type))
                ? shape
                : CodecFor(shape);
        }

        if (CollectionElement(type) is { } element)
            return CodecFor(Instantiate(typeof(CustomCollectionShape<,>).MakeGenericType(type, element)));

        return Instantiate(typeof(ObjectCodec<>).MakeGenericType(type));
    }

    private static object ForArray(Type type)
    {
        var element = type.GetElementType()!;

        if (type.IsSZArray)
            return CodecFor(Instantiate(typeof(ArrayView<>).MakeGenericType(element)));

        if (type.GetArrayRank() > 1)
            return CodecFor(Instantiate(typeof(MultiDimensionalArrayFormatter<,>).MakeGenericType(type, element)));

        throw new NotSupportedException("An array with a non-zero lower bound has no encoding.");
    }

    /// <summary>The engine's codec that drives <paramref name="shape"/>: its interface decides which.</summary>
    private static object CodecFor(object shape)
    {
        foreach (var contract in shape.GetType().GetInterfaces())
        {
            if (!contract.IsGenericType)
                continue;

            var definition = contract.GetGenericTypeDefinition();
            var arguments = contract.GetGenericArguments();

            if (definition == typeof(ISequenceShape<,,,>))
                return Instantiate(typeof(SequenceCodec<,,,>).MakeGenericType(arguments), shape);

            if (definition == typeof(IMapShape<,,,,>))
                return Instantiate(typeof(MapCodec<,,,,>).MakeGenericType(arguments), shape);

            if (definition == typeof(IArrayShape<,>))
                return Instantiate(typeof(ArrayCodec<,>).MakeGenericType(arguments), shape);

            if (definition == typeof(ICompositeFormatter<>))
                return Instantiate(typeof(CompositeCodec<>).MakeGenericType(arguments), shape);
        }

        throw new NotSupportedException($"'{shape.GetType()}' is not a shape the engine can drive.");
    }

    /// <summary>
    /// The element type of a concrete collection the engine can build: not an interface, not
    /// abstract, implementing <c>ICollection&lt;T&gt;</c>, with a public parameterless constructor.
    /// </summary>
    private static Type? CollectionElement(Type type)
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

    private static object Instantiate(Type type, params object?[] arguments) =>
        Activator.CreateInstance(type, arguments)!;
}
