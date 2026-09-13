namespace ViShap.Viper.Formatters;

internal static class PrimitiveFormatters
{
    public static readonly ITypeFormatter Bool = new PrimitiveFormatter<bool>((w, v) => w.Write(v), r => r.ReadBoolean());
    public static readonly ITypeFormatter Byte = new PrimitiveFormatter<byte>((w, v) => w.Write(v), r => r.ReadByte());
    public static readonly ITypeFormatter SByte = new PrimitiveFormatter<sbyte>((w, v) => w.Write(v), r => r.ReadSByte());
    public static readonly ITypeFormatter Short = new PrimitiveFormatter<short>((w, v) => w.Write(v), r => r.ReadInt16());
    public static readonly ITypeFormatter UShort = new PrimitiveFormatter<ushort>((w, v) => w.Write(v), r => r.ReadUInt16());
    public static readonly ITypeFormatter Int = new PrimitiveFormatter<int>((w, v) => w.Write(v), r => r.ReadInt32());
    public static readonly ITypeFormatter UInt = new PrimitiveFormatter<uint>((w, v) => w.Write(v), r => r.ReadUInt32());
    public static readonly ITypeFormatter Long = new PrimitiveFormatter<long>((w, v) => w.Write(v), r => r.ReadInt64());
    public static readonly ITypeFormatter ULong = new PrimitiveFormatter<ulong>((w, v) => w.Write(v), r => r.ReadUInt64());
    public static readonly ITypeFormatter Float = new PrimitiveFormatter<float>((w, v) => w.Write(v), r => r.ReadSingle());
    public static readonly ITypeFormatter Double = new PrimitiveFormatter<double>((w, v) => w.Write(v), r => r.ReadDouble());
    public static readonly ITypeFormatter Decimal = new PrimitiveFormatter<decimal>((w, v) => w.Write(v), r => r.ReadDecimal());
    public static readonly ITypeFormatter Char = new PrimitiveFormatter<char>((w, v) => w.Write(v), r => r.ReadChar());
    public static readonly ITypeFormatter String = new PrimitiveFormatter<string>((w, v) => w.Write(v), r => r.ReadString());
    public static readonly ITypeFormatter Enum = new EnumFormatter();
    public static readonly ITypeFormatter Half = new HalfFormatter();
    public static readonly ITypeFormatter Int128 = new Int128Formatter();
    public static readonly ITypeFormatter UInt128 = new UInt128Formatter();
    public static readonly ITypeFormatter IntPtr = new IntPtrFormatter();
    public static readonly ITypeFormatter UIntPtr = new UIntPtrFormatter();
    public static readonly ITypeFormatter Rune = new RuneFormatter();
    public static readonly ITypeFormatter BigInteger = new BigIntegerFormatter();
}

internal static class TimeFormatters
{
    public static readonly ITypeFormatter DateTime = new DateTimeFormatter();
    public static readonly ITypeFormatter DateTimeOffset = new DateTimeOffsetFormatter();
    public static readonly ITypeFormatter TimeSpan = new TimeSpanFormatter();
    public static readonly ITypeFormatter DateOnly = new DateOnlyFormatter();
    public static readonly ITypeFormatter TimeOnly = new TimeOnlyFormatter();
    public static readonly ITypeFormatter TimeZoneInfo = new TimeZoneInfoFormatter();
}

internal static class NumericFormatters
{
    public static readonly ITypeFormatter Complex = new ComplexFormatter();
    public static readonly ITypeFormatter Plane = new PlaneFormatter();
    public static readonly ITypeFormatter Quaternion = new QuaternionFormatter();
    public static readonly ITypeFormatter Matrix3x2 = new Matrix3x2Formatter();
    public static readonly ITypeFormatter Matrix4x4 = new Matrix4x4Formatter();
    public static readonly ITypeFormatter Vector2 = new Vector2Formatter();
    public static readonly ITypeFormatter Vector3 = new Vector3Formatter();
    public static readonly ITypeFormatter Vector4 = new Vector4Formatter();
}

internal static class TextFormatters
{
    public static readonly ITypeFormatter StringBuilder = new StringBuilderFormatter();
    public static readonly ITypeFormatter CultureInfo = new CultureInfoFormatter();
}

internal static class SystemFormatters
{
    public static readonly ITypeFormatter Guid = new GuidFormatter();
    public static readonly ITypeFormatter Uri = new UriFormatter();
    public static readonly ITypeFormatter Version = new VersionFormatter();
    public static readonly ITypeFormatter BitArray = new BitArrayFormatter();
    public static readonly ITypeFormatter Lazy = new LazyFormatter();
}

internal static class ArrayFormatters
{
    public static readonly ITypeFormatter Array = new ArrayFormatter();
    public static readonly ITypeFormatter MultiDimensionalArray = new MultiDimensionalArrayFormatter();
}

internal static class MemoryFormatters
{
    public static readonly ITypeFormatter Memory = new MemoryFormatter();
    public static readonly ITypeFormatter ReadOnlyMemory = new ReadOnlyMemoryFormatter();
    public static readonly ITypeFormatter ArraySegment = new ArraySegmentFormatter();
    public static readonly ITypeFormatter ReadOnlySequence = new ReadOnlySequenceFormatter();
}

internal static class TupleFormatters
{
    public static readonly ITypeFormatter KeyValuePair = new KeyValuePairFormatter();
    public static readonly ITypeFormatter Tuple = new TupleFormatter();
}

internal static class CollectionFormatters
{
    public static readonly ITypeFormatter List = new ListFormatter();
    public static readonly ITypeFormatter HashSet = new HashSetFormatter();
    public static readonly ITypeFormatter SortedSet = new SortedSetFormatter();
    public static readonly ITypeFormatter LinkedList = new LinkedListFormatter();
    public static readonly ITypeFormatter ObservableCollection = new ObservableCollectionFormatter();
    public static readonly ITypeFormatter Stack = new StackFormatter();
    public static readonly ITypeFormatter Queue = new QueueFormatter();
    public static readonly ITypeFormatter PriorityQueue = new PriorityQueueFormatter();
    public static readonly ITypeFormatter Custom = new CustomCollectionFormatter();
}

internal static class ConcurrentFormatters
{
    public static readonly ITypeFormatter ConcurrentBag = new ConcurrentBagFormatter();
    public static readonly ITypeFormatter ConcurrentQueue = new ConcurrentQueueFormatter();
    public static readonly ITypeFormatter ConcurrentStack = new ConcurrentStackFormatter();
    public static readonly ITypeFormatter ConcurrentDictionary = new ConcurrentDictionaryFormatter();
}

internal static class DictionaryFormatters
{
    public static readonly ITypeFormatter SortedDictionary = new SortedDictionaryFormatter();
    public static readonly ITypeFormatter SortedList = new SortedListFormatter();
    public static readonly ITypeFormatter Dictionary = new DictionaryFormatter();
}

internal static class ReadOnlyFormatters
{
    public static readonly ITypeFormatter ReadOnlyCollection = new ReadOnlyCollectionFormatter();
    public static readonly ITypeFormatter ReadOnlyDictionary = new ReadOnlyDictionaryFormatter();
    public static readonly ITypeFormatter ReadOnlyObservableCollection = new ReadOnlyObservableCollectionFormatter();
}

internal static class ImmutableFormatters
{
    public static readonly ITypeFormatter ImmutableArray = new ImmutableArrayFormatter();
    public static readonly ITypeFormatter ImmutableList = new ImmutableListFormatter();
    public static readonly ITypeFormatter ImmutableHashSet = new ImmutableHashSetFormatter();
    public static readonly ITypeFormatter ImmutableSortedSet = new ImmutableSortedSetFormatter();
    public static readonly ITypeFormatter ImmutableStack = new ImmutableStackFormatter();
    public static readonly ITypeFormatter ImmutableQueue = new ImmutableQueueFormatter();
    public static readonly ITypeFormatter ImmutableDictionary = new ImmutableDictionaryFormatter();
    public static readonly ITypeFormatter ImmutableSortedDictionary = new ImmutableSortedDictionaryFormatter();
}

internal static class FrozenFormatters
{
    public static readonly ITypeFormatter FrozenDictionary = new FrozenDictionaryFormatter();
    public static readonly ITypeFormatter FrozenSet = new FrozenSetFormatter();
}

internal static class UnsupportedTypeFormatters
{
    public static readonly ITypeFormatter Delegate = new DelegateFormatter();
}