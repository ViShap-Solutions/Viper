using System.Buffers;

namespace ViShap.Viper.Engine;

/// <summary>
/// The traversal of one payload: whether it carries reference framing, the reference table of the
/// direction it is travelling in, and — writing without references — the ancestor stack a cycle is
/// found on. It lives in the operation's state, is opened by the pipeline for one payload with
/// <see cref="BeginWrite"/> or <see cref="BeginRead"/>, and is closed with <see cref="End"/>, which
/// returns every pooled part cleared.
/// </summary>
internal struct GraphState
{
    private const int FirstAncestorCapacity = 16;

    private object?[]? _ancestors;
    private int _ancestorCount;

    /// <summary>The write-side reference table, when the payload carries reference framing.</summary>
    public WriteReferenceTable? Written { get; private set; }

    /// <summary>The read-side reference table, when the payload carries reference framing.</summary>
    public ReadReferenceTable? Read { get; private set; }

    /// <summary>
    /// Whether a write searches the ancestor stack for cycles, which it does exactly when it has no
    /// reference framing to express one.
    /// </summary>
    public bool DetectsCycles { get; private set; }

    public void BeginWrite(bool preserveReferences)
    {
        Written = preserveReferences ? WriteReferenceTable.Rent() : null;
        DetectsCycles = !preserveReferences;
    }

    public void BeginRead(bool preserveReferences) =>
        Read = preserveReferences ? ReadReferenceTable.Rent() : null;

    /// <summary>Returns the reference tables and the ancestor stack, all cleared, to their pools.</summary>
    public void End()
    {
        if (Written is not null)
        {
            WriteReferenceTable.Return(Written);
            Written = null;
        }

        if (Read is not null)
        {
            ReadReferenceTable.Return(Read);
            Read = null;
        }

        if (_ancestors is not null)
        {
            ArrayPool<object?>.Shared.Return(_ancestors, clearArray: true);
            _ancestors = null;
        }

        _ancestorCount = 0;
        DetectsCycles = false;
    }

    /// <summary>
    /// Enters <paramref name="value"/> on the path from the root, refusing it when it is already on
    /// that path: without reference framing a cycle has no finite encoding. The path is never deeper
    /// than the depth budget, which has admitted this value, so a linear search by reference is
    /// bounded by <c>MaxDepth</c> and a value shared between siblings is not mistaken for a cycle.
    /// </summary>
    public void PushAncestor(object value)
    {
        for (int index = 0; index < _ancestorCount; index++)
        {
            if (ReferenceEquals(_ancestors![index], value))
                throw new BinaryTypeException(
                    $"Circular reference detected while serializing '{value.GetType()}' — an object of " +
                    "this type refers back to an ancestor already being written. Enable " +
                    "BinarySerializerOptions.Configure().PreserveReferences(), break the cycle, or " +
                    "exclude one side with [BinaryIgnore].");
        }

        if (_ancestors is null || _ancestorCount == _ancestors.Length)
            GrowAncestors();

        _ancestors![_ancestorCount++] = value;
    }

    /// <summary>Leaves the value entered last.</summary>
    public void PopAncestor() => _ancestors![--_ancestorCount] = null;

    private void GrowAncestors()
    {
        var grown = ArrayPool<object?>.Shared.Rent(
            _ancestors is null ? FirstAncestorCapacity : _ancestors.Length * 2);

        if (_ancestors is not null)
        {
            _ancestors.AsSpan(0, _ancestorCount).CopyTo(grown);
            ArrayPool<object?>.Shared.Return(_ancestors, clearArray: true);
        }

        _ancestors = grown;
    }
}
