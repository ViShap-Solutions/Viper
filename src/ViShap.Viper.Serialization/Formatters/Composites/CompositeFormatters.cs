using System.Collections;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace ViShap.Viper.Formatters;

internal sealed class KeyValuePairFormatter : ICompositeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    public void Write(CompositeWriter writer, object value, Type declaredType)
    {
        var arguments = declaredType.GetGenericArguments();
        var accessors = DictionaryAccessorCache.GetEntryAccessors(declaredType);
        writer.WriteValue(accessors.KeyGetter(value), arguments[0]);
        writer.WriteValue(accessors.ValueGetter(value), arguments[1]);
    }

    public object Read(CompositeReader reader, Type declaredType)
    {
        var arguments = declaredType.GetGenericArguments();
        var key = reader.ReadValue(arguments[0]);
        var value = reader.ReadValue(arguments[1]);
        return ActivatorCache.GetTwoArgConstructor(declaredType, arguments[0], arguments[1])(key, value);
    }
}

internal sealed class TupleFormatter : ICompositeFormatter
{
    private static readonly HashSet<Type> Definitions =
    [
        typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>),
        typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>),
        typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>),
        typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>),
        typeof(ValueTuple<,,,,,,,>)
    ];

    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && Definitions.Contains(declaredType.GetGenericTypeDefinition());

    public void Write(CompositeWriter writer, object value, Type declaredType)
    {
        var accessors = TupleAccessorCache.GetAccessors(declaredType);
        for (int i = 0; i < accessors.ArgTypes.Length; i++)
            writer.WriteValue(accessors.Getters[i](value), accessors.ArgTypes[i]);
    }

    public object Read(CompositeReader reader, Type declaredType)
    {
        var accessors = TupleAccessorCache.GetAccessors(declaredType);
        var values = new object?[accessors.ArgTypes.Length];
        for (int i = 0; i < accessors.ArgTypes.Length; i++)
            values[i] = reader.ReadValue(accessors.ArgTypes[i]);

        return accessors.Construct(values);
    }
}

internal sealed class LazyFormatter : ICompositeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType && declaredType.GetGenericTypeDefinition() == typeof(Lazy<>);

    public void Write(CompositeWriter writer, object value, Type declaredType) =>
        writer.WriteValue(
            LazyAccessorCache.GetValueGetter(declaredType)(value),
            declaredType.GetGenericArguments()[0]);

    public object Read(CompositeReader reader, Type declaredType)
    {
        var valueType = declaredType.GetGenericArguments()[0];
        return LazyAccessorCache.GetFactory(valueType)(reader.ReadValue(valueType));
    }
}

/// <summary>
/// <c>ImmutableArray&lt;T&gt;</c> carries a "default" state that is distinct from empty, so it is
/// framed by a presence flag ahead of the element count.
/// </summary>
internal sealed class ImmutableArrayFormatter : ICompositeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsGenericType &&
        declaredType.GetGenericTypeDefinition() == typeof(ImmutableArray<>);

    public void Write(CompositeWriter writer, object value, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];

        bool isDefault = (bool)MethodInvokerCache
            .GetInstanceFinalizerInvoker(declaredType, "get_IsDefault")(value);

        writer.WriteFlag(!isDefault);
        if (isDefault)
            return;

        var array = (Array)ImmutableFactoryCache.GetStructFactory(
            typeof(ImmutableCollectionsMarshal), "AsArray", elementType)(value);

        var count = writer.WriteCount(array.Length, CountKind.Array, "ImmutableArray length");
        for (int i = 0; i < count.Value; i++)
            writer.WriteValue(array.GetValue(i), elementType);
    }

    public object Read(CompositeReader reader, Type declaredType)
    {
        var elementType = declaredType.GetGenericArguments()[0];

        if (!reader.ReadFlag())
            return ActivatorCache.CreateInstance(declaredType);

        var count = reader.ReadCount(CountKind.Array, "ImmutableArray length");
        var builder = (IList)SequenceSupport.CreateList(elementType, count.CapacityHint);

        for (int i = 0; i < count.Value; i++)
            builder.Add(reader.ReadValue(elementType));

        return ImmutableFactoryCache.GetArrayFactory(
            typeof(ImmutableCollectionsMarshal), "AsImmutableArray", elementType)(
            SequenceSupport.ToArray(builder, elementType));
    }
}

/// <summary>
/// Arrays of rank &gt; 1. The shape is validated as a whole — every dimension, the overflow-safe
/// product and the element budget — before a single element is read.
/// </summary>
internal sealed class MultiDimensionalArrayFormatter : ICompositeFormatter
{
    public bool CanHandle(Type declaredType) =>
        declaredType.IsArray && declaredType.GetArrayRank() > 1;

    public void Write(CompositeWriter writer, object value, Type declaredType)
    {
        var array = (Array)value;
        var elementType = declaredType.GetElementType()!;

        var lengths = new int[array.Rank];
        for (int dimension = 0; dimension < array.Rank; dimension++)
            lengths[dimension] = array.GetLength(dimension);

        writer.WriteShape(lengths, "Multi-dimensional array");

        foreach (var element in array)
            writer.WriteValue(element, elementType);
    }

    public object Read(CompositeReader reader, Type declaredType)
    {
        var elementType = declaredType.GetElementType()!;
        var shape = reader.ReadShape(declaredType.GetArrayRank(), "Multi-dimensional array");

        var items = (IList)SequenceSupport.CreateList(elementType, shape.Total.CapacityHint);
        for (int i = 0; i < shape.Total.Value; i++)
            items.Add(reader.ReadValue(elementType));

        var array = Array.CreateInstance(elementType, shape.Lengths);
        int flatIndex = 0;
        Fill(array, new int[shape.Lengths.Length], 0, items, ref flatIndex);
        return array;
    }

    private static void Fill(Array array, int[] indices, int dimension, IList items, ref int flatIndex)
    {
        if (dimension == array.Rank)
        {
            array.SetValue(items[flatIndex++], indices);
            return;
        }

        for (int i = 0; i < array.GetLength(dimension); i++)
        {
            indices[dimension] = i;
            Fill(array, indices, dimension + 1, items, ref flatIndex);
        }
    }
}
