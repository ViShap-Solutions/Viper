using System.Collections.Concurrent;

namespace ViShap.Viper.Formatters;

internal static class TypeFormatterRegistry
{
    private static readonly List<ITypeFormatter> BuiltIn =
    [
        // Primitives
        PrimitiveFormatters.Bool, PrimitiveFormatters.Byte, PrimitiveFormatters.SByte,
        PrimitiveFormatters.Short, PrimitiveFormatters.UShort, PrimitiveFormatters.Int,
        PrimitiveFormatters.UInt, PrimitiveFormatters.Long, PrimitiveFormatters.ULong,
        PrimitiveFormatters.Float, PrimitiveFormatters.Double, PrimitiveFormatters.Decimal,
        PrimitiveFormatters.Char, PrimitiveFormatters.String,
        PrimitiveFormatters.Enum, PrimitiveFormatters.Half, 
        PrimitiveFormatters.Int128, PrimitiveFormatters.UInt128,
        PrimitiveFormatters.IntPtr, PrimitiveFormatters.UIntPtr,
        PrimitiveFormatters.Rune, PrimitiveFormatters.BigInteger,

        // Time
        TimeFormatters.DateTime, TimeFormatters.DateTimeOffset, TimeFormatters.TimeSpan,
        TimeFormatters.DateOnly, TimeFormatters.TimeOnly, TimeFormatters.TimeZoneInfo,

        // Numerics
        NumericFormatters.Complex, NumericFormatters.Plane, NumericFormatters.Quaternion,
        NumericFormatters.Matrix3x2, NumericFormatters.Matrix4x4,
        NumericFormatters.Vector2, NumericFormatters.Vector3, NumericFormatters.Vector4,

        // Text
        TextFormatters.StringBuilder, TextFormatters.CultureInfo,

        // System
        SystemFormatters.Guid, SystemFormatters.Uri, SystemFormatters.Version,
        SystemFormatters.BitArray, SystemFormatters.Lazy,

        // Arrays
        ArrayFormatters.Array, ArrayFormatters.MultiDimensionalArray,

        // Memory
        MemoryFormatters.Memory, MemoryFormatters.ReadOnlyMemory, 
        MemoryFormatters.ArraySegment, MemoryFormatters.ReadOnlySequence,

        // Tuples
        TupleFormatters.KeyValuePair, TupleFormatters.Tuple,

        // Collections
        CollectionFormatters.List, CollectionFormatters.HashSet, CollectionFormatters.SortedSet,
        CollectionFormatters.LinkedList, CollectionFormatters.ObservableCollection,
        CollectionFormatters.Stack, CollectionFormatters.Queue, CollectionFormatters.PriorityQueue,

        // Concurrent
        ConcurrentFormatters.ConcurrentBag, ConcurrentFormatters.ConcurrentQueue,
        ConcurrentFormatters.ConcurrentStack, ConcurrentFormatters.ConcurrentDictionary,

        // Dictionaries
        DictionaryFormatters.SortedDictionary, DictionaryFormatters.SortedList,

        // ReadOnly
        ReadOnlyFormatters.ReadOnlyCollection, ReadOnlyFormatters.ReadOnlyDictionary,
        ReadOnlyFormatters.ReadOnlyObservableCollection,

        // Immutable
        ImmutableFormatters.ImmutableArray, ImmutableFormatters.ImmutableList, 
        ImmutableFormatters.ImmutableHashSet, ImmutableFormatters.ImmutableSortedSet, 
        ImmutableFormatters.ImmutableStack, ImmutableFormatters.ImmutableQueue, 
        ImmutableFormatters.ImmutableDictionary, ImmutableFormatters.ImmutableSortedDictionary,

        // Frozen
        FrozenFormatters.FrozenDictionary, FrozenFormatters.FrozenSet,
        
        DictionaryFormatters.Dictionary,
        CollectionFormatters.Custom,
        
        UnsupportedTypeFormatters.Delegate,
        
        NestedFormatter.Instance
    ];

    private static readonly ConcurrentBag<ITypeFormatter> Custom = [];
    private static readonly ConcurrentDictionary<Type, ITypeFormatter> Cache = new();

    public static void Register(ITypeFormatter formatter) => Custom.Add(formatter);

    public static ITypeFormatter Resolve(Type declaredType) =>
        Cache.GetOrAdd(declaredType, static t =>
        {
            var custom = Custom.FirstOrDefault(f => f.CanHandle(t));
            if (custom is not null)
                return custom;

            foreach (var formatter in BuiltIn)
            {
                if (formatter.CanHandle(t))
                    return formatter;
            }

            throw new BinaryTypeException(
                $"Type '{t}' is not supported by any registered binary formatter.");
        });
}