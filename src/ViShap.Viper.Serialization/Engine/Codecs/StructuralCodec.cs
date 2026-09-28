namespace ViShap.Viper.Engine;

/// <summary>
/// The framing every structural value shares — sequences, maps, composites and objects — written and
/// read in one place, so no shape can omit a step of it:
/// <list type="number">
/// <item>the null flag, when the declared type is a reference type;</item>
/// <item>the reference frame, when the payload carries reference framing and the type is a reference
/// type — a first occurrence registers the value, a back reference ends it;</item>
/// <item>one level of depth and one graph node;</item>
/// <item>without reference framing, the ancestor stack a cycle is found on.</item>
/// </list>
/// </summary>
internal abstract class StructuralCodec<T> : Codec<T>
{
    private static readonly bool IsReference = !typeof(T).IsValueType;

    public sealed override void Write(ref WireWriter writer, T value)
    {
        if (IsReference)
        {
            writer.WriteBoolean(value is not null);
            if (value is null)
                return;

            if (writer.State.Graph.Written is { } references)
            {
                object instance = value;
                if (references.TryGetVisibleId(instance, out int existingId))
                {
                    writer.WriteByte(1);
                    writer.WriteInt32(existingId);
                    return;
                }

                writer.WriteByte(0);
                writer.WriteInt32(references.Register(instance));
            }
        }

        ref var state = ref writer.State;
        using var depth = state.Budget.EnterDepth();
        state.Budget.ConsumeObjectGraphNodes(1);

        bool tracksCycles = IsReference && state.Graph.DetectsCycles;
        if (tracksCycles)
            state.Graph.PushAncestor(value!);

        try
        {
            WriteBody(ref writer, value);
        }
        finally
        {
            if (tracksCycles)
                state.Graph.PopAncestor();
        }
    }

    public sealed override T Read(ref WireReader reader)
    {
        int referenceId = -1;

        if (IsReference)
        {
            if (!reader.ReadBoolean())
                return default!;

            if (reader.State.Graph.Read is { } references)
            {
                byte marker = reader.ReadByte();
                int id = reader.ReadInt32();

                if (id < 0)
                    throw new BinaryFormatException($"Reference id {id} must be non-negative.");

                if (marker is not 0 and not 1)
                    throw new BinaryFormatException($"Unknown reference marker {marker}.");

                if (marker == 1)
                    return Resolve(references, id);

                referenceId = id;
            }
        }

        ref var state = ref reader.State;
        using var depth = state.Budget.EnterDepth();
        state.Budget.ConsumeObjectGraphNodes(1);

        return ReadBody(ref reader, referenceId);
    }

    /// <summary>Writes the shape of a value that is not null and not a back reference.</summary>
    protected abstract void WriteBody(ref WireWriter writer, T value);

    /// <summary>
    /// Reads the shape of a value whose frame has been read. <paramref name="referenceId"/> is the id
    /// its first occurrence declared, or <c>-1</c> when the payload carries no reference framing.
    /// </summary>
    protected abstract T ReadBody(ref WireReader reader, int referenceId);

    /// <summary>Makes <paramref name="value"/> the object <paramref name="referenceId"/> resolves to.</summary>
    protected static void Register(ref WireReader reader, int referenceId, object value)
    {
        if (referenceId >= 0)
            reader.State.Graph.Read!.Register(referenceId, value);
    }

    /// <summary>
    /// Replaces the placeholder registered for a value that could not exist before its children were
    /// read with the finished value.
    /// </summary>
    protected static void Complete(ref WireReader reader, int referenceId, object value)
    {
        if (referenceId >= 0)
            reader.State.Graph.Read!.Replace(referenceId, value);
    }

    private static T Resolve(ReadReferenceTable references, int id)
    {
        if (!references.TryResolve(id, out var existing))
            throw new BinaryFormatException(
                $"Reference to object id {id} was not found in the visible object graph. " +
                "References are only valid within the object that defines them and its descendants.");

        if (ReferenceEquals(existing, ReadReferenceTable.Pending))
            throw new BinaryFormatException(
                $"Reference to object id {id} points at an object that is still being constructed — " +
                "a cycle through a container that cannot be created before its elements " +
                "(array, immutable or frozen collection) is not representable.");

        return existing is T value
            ? value
            : throw new BinaryFormatException(
                $"Reference to object id {id} resolves to '{existing!.GetType()}', which is not a " +
                $"'{typeof(T)}'.");
    }
}
