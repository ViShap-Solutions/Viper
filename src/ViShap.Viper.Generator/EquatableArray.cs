using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ViShap.Viper.Generator;

/// <summary>
/// An immutable array compared by its elements, so a model that holds one is equal to another model
/// built from the same input — which is what lets the incremental pipeline skip unchanged work.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(IEnumerable<T> items) => _items = items.ToArray();

    public static EquatableArray<T> Empty => new(Array.Empty<T>());

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public ImmutableArray<T> AsImmutableArray() => _items is null ? ImmutableArray<T>.Empty : ImmutableArray.Create(_items);

    public bool Equals(EquatableArray<T> other)
    {
        var mine = _items ?? Array.Empty<T>();
        var theirs = other._items ?? Array.Empty<T>();
        if (mine.Length != theirs.Length)
            return false;

        for (int i = 0; i < mine.Length; i++)
        {
            if (!mine[i].Equals(theirs[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var item in _items ?? Array.Empty<T>())
            hash = unchecked(hash * 31 + item.GetHashCode());

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
