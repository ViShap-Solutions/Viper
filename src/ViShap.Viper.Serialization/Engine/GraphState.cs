using System.Buffers;
using System.Runtime.CompilerServices;

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

    private const int NearAncestors = 8;

    private Near _near;
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

        if (_ancestorCount > 0)
            _near = default;

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
        int near = Math.Min(_ancestorCount, NearAncestors);
        for (int index = 0; index < near; index++)
        {
            if (ReferenceEquals(_near[index], value))
                throw Cycle(value);
        }

        for (int index = NearAncestors; index < _ancestorCount; index++)
        {
            if (ReferenceEquals(_ancestors![index - NearAncestors], value))
                throw Cycle(value);
        }

        if (_ancestorCount < NearAncestors)
        {
            _near[_ancestorCount++] = value;
            return;
        }

        if (_ancestors is null || _ancestorCount - NearAncestors == _ancestors.Length)
            GrowAncestors();

        _ancestors![_ancestorCount++ - NearAncestors] = value;
    }

    /// <summary>Whether <paramref name="value"/> is on the current path.</summary>
    public readonly bool IsAncestor(object value)
    {
        int near = Math.Min(_ancestorCount, NearAncestors);
        for (int index = 0; index < near; index++)
        {
            if (ReferenceEquals(_near[index], value))
                return true;
        }

        for (int index = NearAncestors; index < _ancestorCount; index++)
        {
            if (ReferenceEquals(_ancestors![index - NearAncestors], value))
                return true;
        }

        return false;
    }

    public void PopAncestor()
    {
        if (--_ancestorCount < NearAncestors)
            _near[_ancestorCount] = null;
        else
            _ancestors![_ancestorCount - NearAncestors] = null;
    }

    private static BinaryTypeException Cycle(object value) =>
        new($"Circular reference detected while serializing '{value.GetType()}' — an object of " +
            "this type refers back to an ancestor already being written. Enable " +
            "BinarySerializerOptions.Configure().PreserveReferences(), break the cycle, or " +
            "exclude one side with [BinaryIgnore].");

    private void GrowAncestors()
    {
        var grown = ArrayPool<object?>.Shared.Rent(
            _ancestors is null ? FirstAncestorCapacity : _ancestors.Length * 2);

        if (_ancestors is not null)
        {
            _ancestors.AsSpan(0, _ancestorCount - NearAncestors).CopyTo(grown);
            ArrayPool<object?>.Shared.Return(_ancestors, clearArray: true);
        }

        _ancestors = grown;
    }

    /// <summary>The first ancestors of the current path, held in the state itself, so a shallow write rents nothing.</summary>
    [InlineArray(NearAncestors)]
    private struct Near
    {
        private object? _element;
    }
}
