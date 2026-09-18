namespace ViShap.Viper.Engine;

/// <summary>
/// Write-side object identity. Ids are unique across the payload, but they are only <em>visible</em>
/// along the ancestor chain: entering a keyed field opens a scope, leaving it closes one. A back
/// reference is therefore never emitted across two sibling keyed fields, which is what lets a reader
/// with an older schema skip a field it does not know without ever meeting a dangling reference.
/// </summary>
internal sealed class WriteReferenceTable
{
    private readonly List<Dictionary<object, int>> _scopes = [new(ReferenceEqualityComparer.Instance)];
    private int _nextId;

    public bool TryGetVisibleId(object value, out int id)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(value, out id))
                return true;
        }

        id = 0;
        return false;
    }

    public int Register(object value)
    {
        int id = _nextId++;
        _scopes[^1][value] = id;
        return id;
    }

    public Scope Enter() => new(this);

    internal readonly ref struct Scope
    {
        private readonly WriteReferenceTable _owner;

        internal Scope(WriteReferenceTable owner)
        {
            _owner = owner;
            owner._scopes.Add(new Dictionary<object, int>(ReferenceEqualityComparer.Instance));
        }

        public void Dispose() => _owner._scopes.RemoveAt(_owner._scopes.Count - 1);
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

    private readonly List<Dictionary<int, object>> _scopes = [[]];

    public bool TryResolve(int id, out object? value)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(id, out var found))
            {
                value = found;
                return true;
            }
        }

        value = null;
        return false;
    }

    public void Register(int id, object value) => _scopes[^1][id] = value;

    public void Replace(int id, object value)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].ContainsKey(id))
            {
                _scopes[i][id] = value;
                return;
            }
        }

        _scopes[^1][id] = value;
    }

    public Scope Enter() => new(this);

    internal readonly ref struct Scope
    {
        private readonly ReadReferenceTable _owner;

        internal Scope(ReadReferenceTable owner)
        {
            _owner = owner;
            owner._scopes.Add([]);
        }

        public void Dispose() => _owner._scopes.RemoveAt(_owner._scopes.Count - 1);
    }
}
