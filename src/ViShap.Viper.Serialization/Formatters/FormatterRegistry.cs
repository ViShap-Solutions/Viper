using System.Collections.Concurrent;

namespace ViShap.Viper.Formatters;

/// <summary>
/// Resolves the formatter for a type. The list is ordered: the first formatter that claims a type
/// wins, so specific shapes precede general ones. A <c>null</c> result means "no dedicated encoding",
/// which is the object shape the engine handles through <see cref="TypeContract"/> — there is no
/// catch-all formatter that could shadow a more specific one by accident.
/// </summary>
internal static class FormatterRegistry
{
    private static readonly ITypeFormatter[] BuiltIn =
    [
        // Rejections first: a delegate must never be claimed by a collection or object shape.
        new DelegateFormatter(),

        // Scalars.
        new PrimitiveFormatter<bool>((w, v) => w.WriteBoolean(v), r => r.ReadBoolean()),
        new PrimitiveFormatter<byte>((w, v) => w.WriteByte(v), r => r.ReadByte()),
        new PrimitiveFormatter<sbyte>((w, v) => w.WriteSByte(v), r => r.ReadSByte()),
        new PrimitiveFormatter<short>((w, v) => w.WriteInt16(v), r => r.ReadInt16()),
        new PrimitiveFormatter<ushort>((w, v) => w.WriteUInt16(v), r => r.ReadUInt16()),
        new PrimitiveFormatter<int>((w, v) => w.WriteInt32(v), r => r.ReadInt32()),
        new PrimitiveFormatter<uint>((w, v) => w.WriteUInt32(v), r => r.ReadUInt32()),
        new PrimitiveFormatter<long>((w, v) => w.WriteInt64(v), r => r.ReadInt64()),
        new PrimitiveFormatter<ulong>((w, v) => w.WriteUInt64(v), r => r.ReadUInt64()),
        new PrimitiveFormatter<float>((w, v) => w.WriteSingle(v), r => r.ReadSingle()),
        new PrimitiveFormatter<double>((w, v) => w.WriteDouble(v), r => r.ReadDouble()),
        new PrimitiveFormatter<decimal>((w, v) => w.WriteDecimal(v), r => r.ReadDecimal()),
        new PrimitiveFormatter<char>((w, v) => w.WriteChar(v), r => r.ReadChar()),
        new StringFormatter(),
        new EnumFormatter(),
        new HalfFormatter(),
        new Int128Formatter(),
        new UInt128Formatter(),
        new IntPtrFormatter(),
        new UIntPtrFormatter(),
        new RuneFormatter(),
        new BigIntegerFormatter(),

        new DateTimeFormatter(),
        new DateTimeOffsetFormatter(),
        new TimeSpanFormatter(),
        new DateOnlyFormatter(),
        new TimeOnlyFormatter(),
        new TimeZoneInfoFormatter(),

        new ComplexFormatter(),
        new Vector2Formatter(),
        new Vector3Formatter(),
        new Vector4Formatter(),
        new QuaternionFormatter(),
        new PlaneFormatter(),
        new Matrix3x2Formatter(),
        new Matrix4x4Formatter(),

        new GuidFormatter(),
        new UriFormatter(),
        new VersionFormatter(),
        new StringBuilderFormatter(),
        new CultureInfoFormatter(),
        new BitArrayFormatter(),

        // Composites.
        new KeyValuePairFormatter(),
        new TupleFormatter(),
        new LazyFormatter(),
        new ImmutableArrayFormatter(),
        new MultiDimensionalArrayFormatter(),

        // Sequences.
        new ArrayFormatter(),
        new MemoryLikeFormatter(),
        new ReadOnlySequenceFormatter(),
        new ListFormatter(),
        new HashSetFormatter(),
        new SortedSetFormatter(),
        new LinkedListFormatter(),
        new ObservableCollectionFormatter(),
        new StackFormatter(),
        new QueueFormatter(),
        new ConcurrentBagFormatter(),
        new ConcurrentQueueFormatter(),
        new ConcurrentStackFormatter(),
        new ReadOnlyObservableCollectionFormatter(),
        new ReadOnlyCollectionFormatter(),
        new ImmutableListFormatter(),
        new ImmutableHashSetFormatter(),
        new ImmutableSortedSetFormatter(),
        new ImmutableQueueFormatter(),
        new ImmutableStackFormatter(),
        new FrozenSetFormatter(),

        // Maps.
        new ReadOnlyDictionaryFormatter(),
        new FrozenDictionaryFormatter(),
        new ImmutableDictionaryFormatter(),
        new ImmutableSortedDictionaryFormatter(),
        new ConcurrentDictionaryFormatter(),
        new SortedDictionaryFormatter(),
        new SortedListFormatter(),
        new PriorityQueueFormatter(),
        new DictionaryFormatter(),

        // Last resort before the object shape: any concrete ICollection<T> with Add().
        new CustomCollectionFormatter()
    ];

    private static readonly ConcurrentDictionary<Type, ITypeFormatter?> Cache = new();

    public static ITypeFormatter? Resolve(Type declaredType) =>
        Cache.GetOrAdd(declaredType, static type =>
        {
            foreach (var formatter in BuiltIn)
            {
                if (formatter.CanHandle(type))
                    return formatter;
            }

            return null;
        });

    internal static int CachedTypeCount => Cache.Count;
}
