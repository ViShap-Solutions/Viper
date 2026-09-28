namespace ViShap.Viper.Engine;

/// <summary>
/// The engine's codec for a map: the entry count, the loop over the entries and every check around
/// them, with the same division of labour as <see cref="SequenceCodec{TCollection, TElement, TBuilder, TEnumerator}"/>.
/// </summary>
internal sealed class MapCodec<TMap, TKey, TValue, TBuilder, TEnumerator>(
    IMapShape<TMap, TKey, TValue, TBuilder, TEnumerator> shape) : StructuralCodec<TMap>
    where TEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>
{
    /// <summary>
    /// Read on use rather than kept: a type that contains itself resolves its own codec while this
    /// one is being built.
    /// </summary>
    private static Codec<TKey> KeyCodec => FormatterCache<TKey>.Instance;
    /// <summary>
    /// Read on use rather than kept: a type that contains itself resolves its own codec while this
    /// one is being built.
    /// </summary>
    private static Codec<TValue> ValueCodec => FormatterCache<TValue>.Instance;

    public override CodecShape Shape => CodecShape.Map;

    protected override bool FoldsNull => true;

    protected override void WriteBody(ref WireWriter writer, TMap value, bool nullFolded)
    {
        if (shape.CountOf(value) is { } known)
        {
            var count = writer.WriteCount(known, shape.CountKind, shape.CountName, nullFolded);

            int written = 0;
            var entries = shape.GetEnumerator(value);
            try
            {
                while (entries.MoveNext())
                {
                    if (written == count.Value)
                        throw Elements.CountChanged(shape.CountName, typeof(TMap));

                    var entry = entries.Current;
                    KeyCodec.Write(ref writer, entry.Key);
                    ValueCodec.Write(ref writer, entry.Value);
                    written++;
                }
            }
            finally
            {
                entries.Dispose();
            }

            if (written != count.Value)
                throw Elements.CountChanged(shape.CountName, typeof(TMap));

            return;
        }

        var gathered = Gather(ref writer, value);
        try
        {
            writer.WriteCount(gathered.Count, shape.CountKind, shape.CountName, nullFolded);
            foreach (var entry in gathered.Items)
            {
                KeyCodec.Write(ref writer, entry.Key);
                ValueCodec.Write(ref writer, entry.Value);
            }
        }
        finally
        {
            gathered.Dispose();
        }
    }

    protected override TMap ReadBody(ref WireReader reader, int referenceId, bool nullFolded)
    {
        var count = reader.ReadCount(shape.CountKind, shape.CountName, nullFolded);
        var trace = reader.State.Trace;
        trace?.Shape(TraceShape.Map, count.Value);

        var builder = shape.Create(
            count.CapacityFor(KeyCodec.MinimumWireSize + ValueCodec.MinimumWireSize, reader.Remaining));
        if (referenceId >= 0)
            Register(ref reader, referenceId, shape.BuilderIsInstance ? builder! : ReadReferenceTable.Pending);

        for (int i = 0; i < count.Value; i++)
        {
            trace?.LabelMapKey(i);
            var key = KeyCodec.Read(ref reader);
            trace?.LabelMapValue(i);
            var value = ValueCodec.Read(ref reader);
            try
            {
                shape.Add(ref builder, key, value);
            }
            catch (ArgumentException ex)
            {
                throw Elements.Refused(shape.CountName, typeof(TMap), ex);
            }
        }

        TMap completed;
        try
        {
            completed = shape.Complete(builder);
        }
        catch (ArgumentException ex)
        {
            throw Elements.Refused(shape.CountName, typeof(TMap), ex);
        }

        Elements.RequireMaterialized(shape.CountOf(completed), count.Value, shape.CountName, typeof(TMap));

        if (referenceId >= 0 && !shape.BuilderIsInstance)
            Complete(ref reader, referenceId, completed!);

        return completed;
    }

    private ElementBuffer<KeyValuePair<TKey, TValue>> Gather(ref WireWriter writer, TMap value)
    {
        long maximum = Elements.MaximumFor(ref writer.State, shape.CountKind);
        var gathered = new ElementBuffer<KeyValuePair<TKey, TValue>>(0);
        try
        {
            var entries = shape.GetEnumerator(value);
            try
            {
                while (entries.MoveNext())
                {
                    if (gathered.Count >= maximum)
                        throw Elements.TooMany(shape.CountName, maximum);

                    gathered.Add(entries.Current);
                }
            }
            finally
            {
                entries.Dispose();
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
