namespace ViShap.Viper.Engine;

/// <summary>
/// The engine's codec for a sequence: the count, the loop over the elements and every check around
/// them. The shape only counts, enumerates, builds and completes; it never sees a count read from the
/// wire, so it cannot size anything by one.
/// </summary>
internal sealed class SequenceCodec<TCollection, TElement, TBuilder, TEnumerator>(
    ISequenceShape<TCollection, TElement, TBuilder, TEnumerator> shape) : StructuralCodec<TCollection>
    where TEnumerator : IEnumerator<TElement>
{
    /// <summary>
    /// Read on use rather than kept: a type that contains itself resolves its own codec while this
    /// one is being built.
    /// </summary>
    private static Codec<TElement> ElementCodec => FormatterCache<TElement>.Instance;

    public override CodecShape Shape => CodecShape.Sequence;

    protected override bool FoldsNull(ref OperationState state) => true;

    protected override void WriteBody(ref WireWriter writer, TCollection value, bool nullFolded)
    {
        if (shape.TryGetSpan(value, out var span))
        {
            writer.WriteCount(span.Length, shape.CountKind, shape.CountName, nullFolded);
            Elements.Write(ref writer, span);
            return;
        }

        if (!shape.ReverseOnWrite && shape.CountOf(value) is { } known)
        {
            var count = writer.WriteCount(known, shape.CountKind, shape.CountName, nullFolded);

            int written = 0;
            var elements = shape.GetEnumerator(value);
            try
            {
                while (elements.MoveNext())
                {
                    if (written == count.Value)
                        throw Elements.CountChanged(shape.CountName, typeof(TCollection));

                    ElementCodec.Write(ref writer, elements.Current);
                    written++;
                }
            }
            finally
            {
                elements.Dispose();
            }

            if (written != count.Value)
                throw Elements.CountChanged(shape.CountName, typeof(TCollection));

            return;
        }

        var gathered = Gather(ref writer, value);
        try
        {
            writer.WriteCount(gathered.Count, shape.CountKind, shape.CountName, nullFolded);

            var items = gathered.Items;
            if (shape.ReverseOnWrite)
            {
                for (int i = items.Length - 1; i >= 0; i--)
                    ElementCodec.Write(ref writer, items[i]);
            }
            else
            {
                foreach (var item in items)
                    ElementCodec.Write(ref writer, item);
            }
        }
        finally
        {
            gathered.Dispose();
        }
    }

    protected override TCollection ReadBody(ref WireReader reader, int referenceId, bool nullFolded)
    {
        var count = reader.ReadCount(shape.CountKind, shape.CountName, nullFolded);
        var trace = reader.State.Trace;
        trace?.Shape(TraceShape.Sequence, count.Value);

        var builder = shape.Create(count.CapacityFor(ElementCodec.MinimumWireSize, reader.Remaining));
        if (referenceId >= 0)
            Register(ref reader, referenceId, shape.BuilderIsInstance ? builder! : ReadReferenceTable.Pending);

        for (int i = 0; i < count.Value; i++)
        {
            trace?.LabelIndex(i);
            var element = ElementCodec.Read(ref reader);
            try
            {
                shape.Add(ref builder, element);
            }
            catch (ArgumentException ex)
            {
                throw Elements.Refused(shape.CountName, typeof(TCollection), ex);
            }
        }

        TCollection completed;
        try
        {
            completed = shape.Complete(builder);
        }
        catch (ArgumentException ex)
        {
            throw Elements.Refused(shape.CountName, typeof(TCollection), ex);
        }

        Elements.RequireMaterialized(shape.CountOf(completed), count.Value, shape.CountName, typeof(TCollection));

        if (referenceId >= 0 && !shape.BuilderIsInstance)
            Complete(ref reader, referenceId, completed!);

        return completed;
    }

    /// <summary>
    /// Takes at most the configured maximum of elements out of a sequence that has no O(1) count, or
    /// whose elements go on the wire in reverse. A sequence that is too long — or infinite — is
    /// rejected as soon as it crosses the limit, never after being fully enumerated.
    /// </summary>
    private ElementBuffer<TElement> Gather(ref WireWriter writer, TCollection value)
    {
        long maximum = Elements.MaximumFor(ref writer.State, shape.CountKind);
        var gathered = new ElementBuffer<TElement>(0);
        try
        {
            var elements = shape.GetEnumerator(value);
            try
            {
                while (elements.MoveNext())
                {
                    if (gathered.Count >= maximum)
                        throw Elements.TooMany(shape.CountName, maximum);

                    gathered.Add(elements.Current);
                }
            }
            finally
            {
                elements.Dispose();
            }

            return gathered;
        }
        catch
        {
            gathered.Dispose();
            throw;
        }
    }
}
