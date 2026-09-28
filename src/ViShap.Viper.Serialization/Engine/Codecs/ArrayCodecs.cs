using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace ViShap.Viper.Engine;

/// <summary>
/// The engine's codec for a sequence whose elements lie in one contiguous array: the count, then the
/// elements, written from a span and read into an array of their final length (see
/// <see cref="Elements.ReadArray{T}"/>).
/// </summary>
internal sealed class ArrayCodec<TCollection, TElement>(IArrayShape<TCollection, TElement> shape)
    : StructuralCodec<TCollection>
{
    public override CodecShape Shape => CodecShape.Sequence;

    protected override bool FoldsNull => true;

    protected override void WriteBody(ref WireWriter writer, TCollection value, bool nullFolded)
    {
        var elements = shape.Elements(value);
        writer.WriteCount(elements.Length, shape.CountKind, shape.CountName, nullFolded);
        Elements.Write(ref writer, elements);
    }

    protected override TCollection ReadBody(ref WireReader reader, int referenceId, bool nullFolded)
    {
        var count = reader.ReadCount(shape.CountKind, shape.CountName, nullFolded);
        reader.State.Trace?.Shape(TraceShape.Sequence, count.Value);
        if (referenceId >= 0)
            Register(ref reader, referenceId, ReadReferenceTable.Pending);

        var value = shape.Wrap(Elements.ReadArray<TElement>(ref reader, count));

        if (referenceId >= 0)
            Complete(ref reader, referenceId, value!);

        return value;
    }
}

/// <summary>
/// <see cref="ImmutableArray{T}"/>, whose default state is distinct from empty. It is a value type and
/// cannot be null, so the zero of its count stands for the default instance: <c>0</c> is default, and
/// any other value is the count plus one, followed by the elements. Its backing array is reached
/// through <see cref="ImmutableCollectionsMarshal"/> in both directions, so it is neither copied on the
/// way out nor on the way in.
/// </summary>
internal sealed class ImmutableArrayCodec<TElement> : StructuralCodec<ImmutableArray<TElement>>
{
    private const string CountName = "ImmutableArray length";

    public override CodecShape Shape => CodecShape.Sequence;

    protected override void WriteBody(ref WireWriter writer, ImmutableArray<TElement> value, bool nullFolded)
    {
        if (value.IsDefault)
        {
            writer.WriteNull();
            return;
        }

        var elements = value.AsSpan();
        writer.WriteCount(elements.Length, CountKind.Array, CountName, nullFolded: true);
        Elements.Write(ref writer, elements);
    }

    protected override ImmutableArray<TElement> ReadBody(ref WireReader reader, int referenceId, bool nullFolded)
    {
        if (reader.TryReadNull())
        {
            reader.State.Trace?.Null();
            return default;
        }

        var count = reader.ReadCount(CountKind.Array, CountName, nullFolded: true);
        reader.State.Trace?.Shape(TraceShape.Sequence, count.Value);
        return ImmutableCollectionsMarshal.AsImmutableArray(Elements.ReadArray<TElement>(ref reader, count));
    }
}
