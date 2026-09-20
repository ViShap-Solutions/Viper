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
