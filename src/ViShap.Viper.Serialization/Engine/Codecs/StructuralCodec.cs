namespace ViShap.Viper.Engine;

/// <summary>
/// The framing every structural value shares — sequences, maps, composites and objects — written and
/// read in one place, so no shape can omit a step of it:
/// <list type="number">
/// <item>when the payload carries reference framing and the type is a reference type, the reference
/// frame: one number that is zero for null, and otherwise names the value's id and whether this is its
/// first occurrence, which registers it, or a back reference, which ends it;</item>
/// <item>without reference framing, when the type is a reference type, its null: the zero of the
/// value's first number when the shape begins with one (<see cref="FoldsNull"/>), and otherwise a flag
/// byte;</item>
/// <item>one level of depth and one graph node;</item>
/// <item>without reference framing, the ancestor stack a cycle is found on.</item>
/// </list>
/// Null is therefore written exactly once, in the first number the value begins with.
/// </summary>
internal abstract class StructuralCodec<T> : Codec<T>
{
    private static readonly bool IsReference = !typeof(T).IsValueType;

    /// <summary>
    /// Whether the shape begins with a structural number — a count, a keyed field count, an array
    /// rank — which then carries the value's null when the value is not reference-framed. A shape
    /// that begins otherwise carries it in a flag byte.
    /// </summary>
    protected virtual bool FoldsNull => false;

    public sealed override void Write(ref WireWriter writer, T value)
    {
        bool nullFolded = false;

        if (IsReference)
        {
            if (writer.State.Graph.Written is { } references)
            {
                if (value is null)
                {
                    writer.WriteNull();
                    return;
                }

                object instance = value;
                if (references.TryGetVisibleId(instance, out int existingId))
                {
                    ReferenceFrame.WriteBackReference(ref writer, existingId);
                    return;
                }

                ReferenceFrame.WriteFirstOccurrence(ref writer, references.Register(instance));
            }
            else if (value is null)
            {
                writer.WriteNull();
                return;
            }
            else if (FoldsNull)
            {
                nullFolded = true;
            }
            else
            {
                writer.WriteBoolean(true);
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
            WriteBody(ref writer, value, nullFolded);
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
        bool nullFolded = false;
        var trace = reader.State.Trace;
        trace?.Begin(typeof(T), reader.Position);

        if (IsReference)
        {
            if (reader.State.Graph.Read is { } references)
            {
                var frame = ReferenceFrame.Read(ref reader);
                if (frame.IsNull)
                    return Null(ref reader, trace);

                trace?.Reference(frame.Id, frame.IsBackReference);

                if (frame.IsBackReference)
                {
                    var existing = Resolve(references, frame.Id);
                    trace?.End(reader.Position);
                    return existing;
                }

                referenceId = frame.Id;
            }
            else if (FoldsNull)
            {
                if (reader.TryReadNull())
                    return Null(ref reader, trace);

                nullFolded = true;
            }
            else if (!reader.ReadBoolean())
            {
                return Null(ref reader, trace);
            }
        }

        ref var state = ref reader.State;
        using var depth = state.Budget.EnterDepth();
        state.Budget.ConsumeObjectGraphNodes(1);

        var value = ReadBody(ref reader, referenceId, nullFolded);
        trace?.End(reader.Position);
        return value;
    }

    private static T Null(ref WireReader reader, IWireTrace? trace)
    {
        trace?.Null();
        trace?.End(reader.Position);
        return default!;
    }

    /// <summary>
    /// Writes the shape of a value that is not null and not a back reference. When
    /// <paramref name="nullFolded"/>, the shape's first number carries the value's null and is written
    /// one higher.
    /// </summary>
    protected abstract void WriteBody(ref WireWriter writer, T value, bool nullFolded);

    /// <summary>
    /// Reads the shape of a value whose frame has been read. <paramref name="referenceId"/> is the id
    /// its first occurrence declared, or <c>-1</c> when the payload carries no reference framing; when
    /// <paramref name="nullFolded"/>, the shape's first number was written one higher.
    /// </summary>
    protected abstract T ReadBody(ref WireReader reader, int referenceId, bool nullFolded);

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

/// <summary>
/// The reference frame of a structural reference-typed value, one 7-bit integer: <c>0</c> is null,
/// <c>((id &lt;&lt; 1) | 0) + 1</c> is the first occurrence of <c>id</c>, whose shape follows, and
/// <c>((id &lt;&lt; 1) | 1) + 1</c> a back reference to <c>id</c>, which ends the value.
/// </summary>
internal readonly struct ReferenceFrame
{
    /// <summary>The largest id a frame can carry: twice it plus two still fits the 7-bit integer range.</summary>
    public const int MaxId = (int.MaxValue - 2) / 2;

    private ReferenceFrame(int id, bool isNull, bool isBackReference)
    {
        Id = id;
        IsNull = isNull;
        IsBackReference = isBackReference;
    }

    public int Id { get; }

    public bool IsNull { get; }

    public bool IsBackReference { get; }

    public static void WriteFirstOccurrence(ref WireWriter writer, int id) => Write(ref writer, id, back: 0);

    public static void WriteBackReference(ref WireWriter writer, int id) => Write(ref writer, id, back: 1);

    public static ReferenceFrame Read(ref WireReader reader)
    {
        int frame = reader.Read7BitEncodedInt("reference frame");
        if (frame == 0)
            return new ReferenceFrame(-1, isNull: true, isBackReference: false);

        frame--;
        return new ReferenceFrame(frame >> 1, isNull: false, isBackReference: (frame & 1) == 1);
    }

    private static void Write(ref WireWriter writer, int id, int back)
    {
        if (id > MaxId)
            throw new BinaryLimitException(
                $"Reference id {id} exceeds the largest id a reference frame can carry ({MaxId}).");

        writer.Write7BitEncodedInt(((id << 1) | back) + 1);
    }
}
