namespace ViShap.Viper.Engine;

/// <summary>
/// Write-side object identity. Ids are unique across the payload, but they are only <em>visible</em>
/// along the ancestor chain: entering a keyed field opens a scope, leaving it closes one and forgets
/// every object registered inside it. A back reference is therefore never emitted across two sibling
/// keyed fields, which is what lets a reader with an older schema skip a field it does not know
/// without ever meeting a dangling reference.
/// <para>
/// One operation uses one table, taken with <see cref="Rent"/> and handed back with
/// <see cref="Return"/>, which clears it, so a serializer does not build a new table per call.
/// </para>
/// </summary>
internal sealed class WriteReferenceTable
{
    [ThreadStatic]
    private static WriteReferenceTable? s_cached;

    private readonly Dictionary<object, int> _visible = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _registered = [];
    private readonly List<int> _scopeStarts = [];
    private int _nextId;

    /// <summary>A cleared table, reused when the current thread returned one.</summary>
    public static WriteReferenceTable Rent()
    {
        var table = s_cached ?? new WriteReferenceTable();
        s_cached = null;
        return table;
    }

    /// <summary>
    /// Clears <paramref name="table"/>, so it holds no object of the finished operation, and keeps it
    /// for the next operation on this thread unless it grew past <see cref="ReferencePool.MaxRetainedEntries"/>.
    /// </summary>
    public static void Return(WriteReferenceTable table)
    {
        bool retain = table._visible.EnsureCapacity(0) <= ReferencePool.MaxRetainedEntries;
        table._visible.Clear();
        table._registered.Clear();
        table._scopeStarts.Clear();
        table._nextId = 0;

        if (retain)
            s_cached = table;
    }

    public bool TryGetVisibleId(object value, out int id) => _visible.TryGetValue(value, out id);

    public int Register(object value)
    {
        int id = _nextId++;
        _visible[value] = id;
        _registered.Add(value);
        return id;
    }

    public Scope Enter() => new(this);

    internal readonly ref struct Scope
    {
        private readonly WriteReferenceTable _owner;

        internal Scope(WriteReferenceTable owner)
        {
            _owner = owner;
            owner._scopeStarts.Add(owner._registered.Count);
        }

        public void Dispose()
        {
            int start = _owner._scopeStarts[^1];
            _owner._scopeStarts.RemoveAt(_owner._scopeStarts.Count - 1);

            for (int index = start; index < _owner._registered.Count; index++)
                _owner._visible.Remove(_owner._registered[index]);

            _owner._registered.RemoveRange(start, _owner._registered.Count - start);
        }
    }
}

/// <summary>
/// Read-side object identity, mirroring <see cref="WriteReferenceTable"/>. An id registered while its
/// object is still being built resolves to <see cref="Pending"/>, so a reference into a half-built
/// immutable container fails deterministically instead of yielding a broken instance.
/// </summary>
internal sealed class ReadReferenceTable
{
    internal static readonly object Pending = new();

    [ThreadStatic]
    private static ReadReferenceTable? s_cached;

    private readonly Dictionary<int, object> _visible = [];
    private readonly List<int> _registered = [];
    private readonly List<int> _scopeStarts = [];

    /// <summary>A cleared table, reused when the current thread returned one.</summary>
    public static ReadReferenceTable Rent()
    {
        var table = s_cached ?? new ReadReferenceTable();
        s_cached = null;
        return table;
    }

    /// <summary>
    /// Clears <paramref name="table"/>, so it holds no object of the finished operation, and keeps it
    /// for the next operation on this thread unless it grew past <see cref="ReferencePool.MaxRetainedEntries"/>.
    /// </summary>
    public static void Return(ReadReferenceTable table)
    {
        bool retain = table._visible.EnsureCapacity(0) <= ReferencePool.MaxRetainedEntries;
        table._visible.Clear();
        table._registered.Clear();
        table._scopeStarts.Clear();

        if (retain)
            s_cached = table;
    }

    public bool TryResolve(int id, out object? value)
    {
        bool found = _visible.TryGetValue(id, out var existing);
        value = existing;
        return found;
    }

    /// <summary>
    /// Records a first occurrence. An id that is already visible would give one graph a second
    /// spelling on the wire, so it is refused rather than allowed to overwrite the earlier object.
    /// </summary>
    public void Register(int id, object value)
    {
        if (!_visible.TryAdd(id, value))
            throw new BinaryFormatException(
                $"Reference id {id} is declared more than once in the visible object graph.");

        _registered.Add(id);
    }

    /// <summary>Replaces the object a visible id resolves to, or registers it in the current scope.</summary>
    public void Replace(int id, object value)
    {
        if (_visible.ContainsKey(id))
        {
            _visible[id] = value;
            return;
        }

        _visible[id] = value;
        _registered.Add(id);
    }

    public Scope Enter() => new(this);

    internal readonly ref struct Scope
    {
        private readonly ReadReferenceTable _owner;

        internal Scope(ReadReferenceTable owner)
        {
            _owner = owner;
            owner._scopeStarts.Add(owner._registered.Count);
        }

        public void Dispose()
        {
            int start = _owner._scopeStarts[^1];
            _owner._scopeStarts.RemoveAt(_owner._scopeStarts.Count - 1);

            for (int index = start; index < _owner._registered.Count; index++)
                _owner._visible.Remove(_owner._registered[index]);

            _owner._registered.RemoveRange(start, _owner._registered.Count - start);
        }
    }
}

/// <summary>What the reference tables keep between operations.</summary>
internal static class ReferencePool
{
    /// <summary>
    /// The largest capacity a table may have grown to and still be kept for reuse. A table that grew past it
    /// is left to the collector, so one large graph does not pin its table's capacity to a thread.
    /// </summary>
    public const int MaxRetainedEntries = 4096;
}
