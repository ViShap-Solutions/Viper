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

    /// <summary>The path depth from which the ancestors are also held in an index, so a search stops growing with depth.</summary>
    internal const int IndexedAncestors = 32;

    private Near _near;
    private object?[]? _ancestors;
    private int _ancestorCount;
    private AncestorIndex? _index;

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

    /// <summary>Returns the reference tables, the ancestor stack and its index, all cleared, to their pools.</summary>
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

        if (_index is not null)
        {
            AncestorIndex.Return(_index);
            _index = null;
        }

        if (_ancestorCount > 0)
            _near = default;

        _ancestorCount = 0;
        DetectsCycles = false;
    }

    /// <summary>
    /// Enters <paramref name="value"/> on the path from the root, refusing it when it is already on
    /// that path: without reference framing a cycle has no finite encoding. The path is never deeper
    /// than the depth budget, which has admitted this value, and a value shared between siblings is not
    /// mistaken for a cycle. A shallow path is searched by reference, one ancestor after another; the
    /// first search on a path already <see cref="IndexedAncestors"/> deep puts every ancestor in an
    /// index by reference, which answers the search for the rest of the payload.
    /// </summary>
    public void PushAncestor(object value)
    {
        if (_index is null && _ancestorCount >= IndexedAncestors)
        {
            _index = AncestorIndex.Rent();
            for (int index = 0; index < _ancestorCount; index++)
                _index.Add(At(index));
        }

        if (_index is not null)
        {
            if (!_index.Add(value))
                throw Cycle(value);
        }
        else if (Search(value))
        {
            throw Cycle(value);
        }

        Append(value);
    }

    /// <summary>Whether <paramref name="value"/> is on the current path.</summary>
    public readonly bool IsAncestor(object value) => _index?.Contains(value) ?? Search(value);

    public void PopAncestor()
    {
        _ancestorCount--;
        _index?.Remove(At(_ancestorCount));

        if (_ancestorCount < NearAncestors)
            _near[_ancestorCount] = null;
        else
            _ancestors![_ancestorCount - NearAncestors] = null;
    }

    /// <summary>Searches the path by reference, one ancestor after another.</summary>
    private readonly bool Search(object value)
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

    private void Append(object value)
    {
        if (_ancestorCount < NearAncestors)
        {
            _near[_ancestorCount++] = value;
            return;
        }

        if (_ancestors is null || _ancestorCount - NearAncestors == _ancestors.Length)
            GrowAncestors();

        _ancestors![_ancestorCount++ - NearAncestors] = value;
    }

    /// <summary>The ancestor at <paramref name="index"/> on the path, counted from the root.</summary>
    private readonly object At(int index) =>
        index < NearAncestors ? _near[index]! : _ancestors![index - NearAncestors]!;

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

    /// <summary>
    /// The ancestors of a deep path, by reference. One operation uses one index, taken with
    /// <see cref="Rent"/> once its path is deep enough and handed back with <see cref="Return"/>,
    /// which clears it, so no object of one operation is reachable from the next.
    /// </summary>
    internal sealed class AncestorIndex
    {
        [ThreadStatic]
        private static AncestorIndex? s_cached;

        private readonly HashSet<object> _ancestors = new(ReferenceEqualityComparer.Instance);

        /// <summary>The number of ancestors the index holds.</summary>
        public int Count => _ancestors.Count;

        /// <summary>A cleared index, reused when the current thread returned one.</summary>
        public static AncestorIndex Rent()
        {
            var index = s_cached ?? new AncestorIndex();
            s_cached = null;
            return index;
        }

        /// <summary>
        /// Clears <paramref name="index"/> and keeps it for the next operation on this thread unless it
        /// grew past <see cref="ReferencePool.MaxRetainedEntries"/>.
        /// </summary>
        public static void Return(AncestorIndex index)
        {
            bool retain = index._ancestors.EnsureCapacity(0) <= ReferencePool.MaxRetainedEntries;
            index._ancestors.Clear();

            if (retain)
                s_cached = index;
        }

        /// <summary>The index this thread keeps for its next operation, if it keeps one.</summary>
        internal static AncestorIndex? Cached => s_cached;

        public bool Add(object value) => _ancestors.Add(value);

        public bool Contains(object value) => _ancestors.Contains(value);

        public void Remove(object value) => _ancestors.Remove(value);
    }

    /// <summary>The first ancestors of the current path, held in the state itself, so a shallow write rents nothing.</summary>
    [InlineArray(NearAncestors)]
    private struct Near
    {
        private object? _element;
    }
}
