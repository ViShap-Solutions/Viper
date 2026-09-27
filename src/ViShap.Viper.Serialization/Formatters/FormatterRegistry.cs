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
        new PrimitiveFormatter<bool>(
            (ref WireWriter w, bool v) => w.WriteBoolean(v),
            (ref WireReader r) => r.ReadBoolean()),
        new PrimitiveFormatter<byte>(
            (ref WireWriter w, byte v) => w.WriteByte(v),
            (ref WireReader r) => r.ReadByte()),
        new PrimitiveFormatter<sbyte>(
            (ref WireWriter w, sbyte v) => w.WriteSByte(v),
            (ref WireReader r) => r.ReadSByte()),
        new PrimitiveFormatter<short>(
            (ref WireWriter w, short v) => w.WriteInt16(v),
            (ref WireReader r) => r.ReadInt16()),
        new PrimitiveFormatter<ushort>(
            (ref WireWriter w, ushort v) => w.WriteUInt16(v),
            (ref WireReader r) => r.ReadUInt16()),
        new PrimitiveFormatter<int>(
            (ref WireWriter w, int v) => w.WriteInt32(v),
            (ref WireReader r) => r.ReadInt32()),
        new PrimitiveFormatter<uint>(
            (ref WireWriter w, uint v) => w.WriteUInt32(v),
            (ref WireReader r) => r.ReadUInt32()),
        new PrimitiveFormatter<long>(
            (ref WireWriter w, long v) => w.WriteInt64(v),
            (ref WireReader r) => r.ReadInt64()),
        new PrimitiveFormatter<ulong>(
            (ref WireWriter w, ulong v) => w.WriteUInt64(v),
            (ref WireReader r) => r.ReadUInt64()),
        new PrimitiveFormatter<float>(
            (ref WireWriter w, float v) => w.WriteSingle(v),
            (ref WireReader r) => r.ReadSingle()),
        new PrimitiveFormatter<double>(
            (ref WireWriter w, double v) => w.WriteDouble(v),
            (ref WireReader r) => r.ReadDouble()),
        new PrimitiveFormatter<decimal>(
            (ref WireWriter w, decimal v) => w.WriteDecimal(v),
            (ref WireReader r) => r.ReadDecimal()),
        new PrimitiveFormatter<char>(
            (ref WireWriter w, char v) => w.WriteChar(v),
            (ref WireReader r) => r.ReadChar()),
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
