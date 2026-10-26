using System.Collections;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A concrete <see cref="ICollection{T}"/> with a public parameterless constructor and an
/// <c>Add</c> method, which is what contract §23 asks of a custom collection. It is deliberately not
/// derived from any framework collection, so nothing but the documented shape can claim it.
/// </summary>
public sealed class Bag<T> : ICollection<T>
{
    private readonly List<T> _items = [];

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public void Add(T item) => _items.Add(item);

    public void Clear() => _items.Clear();

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public bool Remove(T item) => _items.Remove(item);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Two interface-typed members that may share one instance, which is what RT-C05 needs to tell
/// preserved identity apart from a preserved concrete type.
/// </summary>
public class SharedInterfaceLists
{
    public IList<int>? A { get; set; }
    public IList<int>? B { get; set; }
}

/// <summary>An interface-typed member holding a value of some other concrete implementation.</summary>
public class InterfaceMember
{
    public IList<int>? Items { get; set; }
    public IDictionary<string, int>? Map { get; set; }
}

/// <summary>A custom collection whose public <c>Add(T)</c> returns a <see langword="bool"/>, inherited from <see cref="HashSet{T}"/>.</summary>
public sealed class SetBag : HashSet<int>;

/// <summary>A custom collection whose public <c>Add(T)</c> returns the collection, for chaining.</summary>
public sealed class ChainBag : ICollection<string>
{
    private readonly List<string> _items = [];

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public ChainBag Add(string item)
    {
        _items.Add(item);
        return this;
    }

    void ICollection<string>.Add(string item) => Add(item);

    public void Clear() => _items.Clear();

    public bool Contains(string item) => _items.Contains(item);

    public void CopyTo(string[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public bool Remove(string item) => _items.Remove(item);

    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A custom collection whose public <c>Add(T)</c> returns the new count, an <see langword="int"/>, and
/// whose <c>ICollection&lt;T&gt;.Add</c> calls it.
/// </summary>
public sealed class TallyBag : ICollection<int>
{
    private readonly List<int> _items = [];

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public int Add(int item)
    {
        _items.Add(item);
        return _items.Count;
    }

    void ICollection<int>.Add(int item) => Add(item);

    public void Clear() => _items.Clear();

    public bool Contains(int item) => _items.Contains(item);

    public void CopyTo(int[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public bool Remove(int item) => _items.Remove(item);

    public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A value-type collection whose count is a field of its own, so an element added to a copy of it is lost
/// to the original.
/// </summary>
public struct TallyStruct : ICollection<int>
{
    private readonly List<int> _items;
    private int _count;

    public TallyStruct() => _items = [];

    public readonly int Count => _count;

    public readonly bool IsReadOnly => false;

    public void Add(int item)
    {
        _items.Add(item);
        _count++;
    }

    public void Clear()
    {
        _items.Clear();
        _count = 0;
    }

    public readonly bool Contains(int item) => _items.Contains(item);

    public readonly void CopyTo(int[] array, int arrayIndex) => _items.GetRange(0, _count).CopyTo(array, arrayIndex);

    public bool Remove(int item) => throw new NotSupportedException();

    public readonly IEnumerator<int> GetEnumerator() => _items.GetRange(0, _count).GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
