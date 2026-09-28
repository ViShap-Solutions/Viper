using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ViShap.Viper.Formatters;

internal sealed class KeyValuePairFormatter<TKey, TValue> : ICompositeFormatter<KeyValuePair<TKey, TValue>>
{
    public void Write(ref CompositeWriter writer, KeyValuePair<TKey, TValue> value)
    {
        writer.WriteValue(value.Key);
        writer.WriteValue(value.Value);
    }

    public KeyValuePair<TKey, TValue> Read(ref CompositeReader reader)
    {
        var key = reader.ReadValue<TKey>();
        var value = reader.ReadValue<TValue>();
        return new KeyValuePair<TKey, TValue>(key, value);
    }
}

/// <summary>
/// <c>Lazy&lt;T&gt;</c> travels as its value, so writing one materializes it. Reading produces a lazy
/// value that already holds the value and never runs a factory.
/// </summary>
internal sealed class LazyFormatter<T> : ICompositeFormatter<Lazy<T>>
{
    public void Write(ref CompositeWriter writer, Lazy<T> value) => writer.WriteValue(value.Value);

    public Lazy<T> Read(ref CompositeReader reader)
    {
        var value = reader.ReadValue<T>();
        return new Lazy<T>(() => value);
    }
}

/// <summary>
/// Arrays of rank &gt; 1. The shape is validated as a whole — every dimension, the overflow-safe
/// product and the element budget — before a single element is read, and the elements travel in
/// row-major order, which is the order an array holds them in memory.
/// </summary>
internal sealed class MultiDimensionalArrayFormatter<TArray, TElement> : ICompositeFormatter<TArray>
    where TArray : class
{
    private const string What = "Multi-dimensional array";

    private static readonly int Rank = typeof(TArray).GetArrayRank();

    public void Write(ref CompositeWriter writer, TArray value)
    {
        var array = (Array)(object)value;

        var lengths = new int[array.Rank];
        for (int dimension = 0; dimension < array.Rank; dimension++)
            lengths[dimension] = array.GetLength(dimension);

        writer.WriteShape(lengths, What);
        writer.WriteElements(Elements(array));
    }

    public TArray Read(ref CompositeReader reader)
    {
        var shape = reader.ReadShape(Rank, What);
        var elements = reader.ReadElements<TElement>(shape.Total);

        var array = Array.CreateInstance(typeof(TElement), shape.Lengths);
        elements.CopyTo(Elements(array));
        return (TArray)(object)array;
    }

    private static Span<TElement> Elements(Array array) =>
        MemoryMarshal.CreateSpan(
            ref Unsafe.As<byte, TElement>(ref MemoryMarshal.GetArrayDataReference(array)),
            array.Length);
}

internal sealed class TupleFormatter<T1> : ICompositeFormatter<Tuple<T1>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1> value)
    {
        writer.WriteValue(value.Item1);
    }

    public Tuple<T1> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        return new Tuple<T1>(item1);
    }
}

internal sealed class TupleFormatter<T1, T2> : ICompositeFormatter<Tuple<T1, T2>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
    }

    public Tuple<T1, T2> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        return new Tuple<T1, T2>(item1, item2);
    }
}

internal sealed class TupleFormatter<T1, T2, T3> : ICompositeFormatter<Tuple<T1, T2, T3>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2, T3> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
    }

    public Tuple<T1, T2, T3> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        return new Tuple<T1, T2, T3>(item1, item2, item3);
    }
}

internal sealed class TupleFormatter<T1, T2, T3, T4> : ICompositeFormatter<Tuple<T1, T2, T3, T4>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2, T3, T4> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
    }

    public Tuple<T1, T2, T3, T4> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        return new Tuple<T1, T2, T3, T4>(item1, item2, item3, item4);
    }
}

internal sealed class TupleFormatter<T1, T2, T3, T4, T5> : ICompositeFormatter<Tuple<T1, T2, T3, T4, T5>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2, T3, T4, T5> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
    }

    public Tuple<T1, T2, T3, T4, T5> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        return new Tuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
    }
}

internal sealed class TupleFormatter<T1, T2, T3, T4, T5, T6> : ICompositeFormatter<Tuple<T1, T2, T3, T4, T5, T6>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2, T3, T4, T5, T6> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
    }

    public Tuple<T1, T2, T3, T4, T5, T6> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        var item6 = reader.ReadValue<T6>();
        return new Tuple<T1, T2, T3, T4, T5, T6>(item1, item2, item3, item4, item5, item6);
    }
}

internal sealed class TupleFormatter<T1, T2, T3, T4, T5, T6, T7> : ICompositeFormatter<Tuple<T1, T2, T3, T4, T5, T6, T7>>
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2, T3, T4, T5, T6, T7> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
    }

    public Tuple<T1, T2, T3, T4, T5, T6, T7> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        var item6 = reader.ReadValue<T6>();
        var item7 = reader.ReadValue<T7>();
        return new Tuple<T1, T2, T3, T4, T5, T6, T7>(item1, item2, item3, item4, item5, item6, item7);
    }
}

internal sealed class TupleFormatter<T1, T2, T3, T4, T5, T6, T7, TRest> : ICompositeFormatter<Tuple<T1, T2, T3, T4, T5, T6, T7, TRest>>
    where TRest : notnull
{
    public void Write(ref CompositeWriter writer, Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
        writer.WriteValue(value.Rest);
    }

    public Tuple<T1, T2, T3, T4, T5, T6, T7, TRest> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        var item6 = reader.ReadValue<T6>();
        var item7 = reader.ReadValue<T7>();
        var item8 = reader.ReadValue<TRest>();
        return new Tuple<T1, T2, T3, T4, T5, T6, T7, TRest>(item1, item2, item3, item4, item5, item6, item7, item8);
    }
}

internal sealed class ValueTupleFormatter<T1> : ICompositeFormatter<ValueTuple<T1>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1> value)
    {
        writer.WriteValue(value.Item1);
    }

    public ValueTuple<T1> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        return new ValueTuple<T1>(item1);
    }
}

internal sealed class ValueTupleFormatter<T1, T2> : ICompositeFormatter<ValueTuple<T1, T2>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
    }

    public ValueTuple<T1, T2> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        return new ValueTuple<T1, T2>(item1, item2);
    }
}

internal sealed class ValueTupleFormatter<T1, T2, T3> : ICompositeFormatter<ValueTuple<T1, T2, T3>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2, T3> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
    }

    public ValueTuple<T1, T2, T3> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        return new ValueTuple<T1, T2, T3>(item1, item2, item3);
    }
}

internal sealed class ValueTupleFormatter<T1, T2, T3, T4> : ICompositeFormatter<ValueTuple<T1, T2, T3, T4>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2, T3, T4> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
    }

    public ValueTuple<T1, T2, T3, T4> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        return new ValueTuple<T1, T2, T3, T4>(item1, item2, item3, item4);
    }
}

internal sealed class ValueTupleFormatter<T1, T2, T3, T4, T5> : ICompositeFormatter<ValueTuple<T1, T2, T3, T4, T5>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2, T3, T4, T5> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
    }

    public ValueTuple<T1, T2, T3, T4, T5> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        return new ValueTuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);
    }
}

internal sealed class ValueTupleFormatter<T1, T2, T3, T4, T5, T6> : ICompositeFormatter<ValueTuple<T1, T2, T3, T4, T5, T6>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2, T3, T4, T5, T6> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
    }

    public ValueTuple<T1, T2, T3, T4, T5, T6> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        var item6 = reader.ReadValue<T6>();
        return new ValueTuple<T1, T2, T3, T4, T5, T6>(item1, item2, item3, item4, item5, item6);
    }
}

internal sealed class ValueTupleFormatter<T1, T2, T3, T4, T5, T6, T7> : ICompositeFormatter<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2, T3, T4, T5, T6, T7> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
    }

    public ValueTuple<T1, T2, T3, T4, T5, T6, T7> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        var item6 = reader.ReadValue<T6>();
        var item7 = reader.ReadValue<T7>();
        return new ValueTuple<T1, T2, T3, T4, T5, T6, T7>(item1, item2, item3, item4, item5, item6, item7);
    }
}

internal sealed class ValueTupleFormatter<T1, T2, T3, T4, T5, T6, T7, TRest> : ICompositeFormatter<ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>>
    where TRest : struct
{
    public void Write(ref CompositeWriter writer, ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> value)
    {
        writer.WriteValue(value.Item1);
        writer.WriteValue(value.Item2);
        writer.WriteValue(value.Item3);
        writer.WriteValue(value.Item4);
        writer.WriteValue(value.Item5);
        writer.WriteValue(value.Item6);
        writer.WriteValue(value.Item7);
        writer.WriteValue(value.Rest);
    }

    public ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> Read(ref CompositeReader reader)
    {
        var item1 = reader.ReadValue<T1>();
        var item2 = reader.ReadValue<T2>();
        var item3 = reader.ReadValue<T3>();
        var item4 = reader.ReadValue<T4>();
        var item5 = reader.ReadValue<T5>();
        var item6 = reader.ReadValue<T6>();
        var item7 = reader.ReadValue<T7>();
        var item8 = reader.ReadValue<TRest>();
        return new ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>(item1, item2, item3, item4, item5, item6, item7, item8);
    }
}
