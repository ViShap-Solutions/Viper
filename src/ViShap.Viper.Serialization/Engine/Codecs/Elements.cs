using System.Buffers;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Engine;

/// <summary>
/// Elements gathered in a buffer rented from <see cref="ArrayPool{T}.Shared"/>, which grows as they
/// arrive and is cleared when it goes back. It is what a count the payload does not back with bytes
/// is read into, and what a sequence without an O(1) count is gathered into before it is written.
/// </summary>
internal struct ElementBuffer<T> : IDisposable
{
    private T[]? _items;
    private int _count;

    public ElementBuffer(int capacity) =>
        _items = capacity > 0 ? ArrayPool<T>.Shared.Rent(capacity) : null;

    public readonly int Count => _count;

    public readonly ReadOnlySpan<T> Items => _items.AsSpan(0, _count);

    public void Add(T item)
    {
        if (_items is null || _count == _items.Length)
            Grow();

        _items![_count++] = item;
    }

    /// <summary>An array holding exactly the elements gathered.</summary>
    public readonly T[] ToArray() => _count == 0 ? [] : Items.ToArray();

    public void Dispose()
    {
        if (_items is not null)
        {
            ArrayPool<T>.Shared.Return(_items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _items = null;
        }

        _count = 0;
    }

    private void Grow()
    {
        var grown = ArrayPool<T>.Shared.Rent(_items is null ? 16 : _items.Length * 2);
        if (_items is not null)
        {
            _items.AsSpan(0, _count).CopyTo(grown);
            ArrayPool<T>.Shared.Return(_items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        _items = grown;
    }
}

/// <summary>The engine's loops over the elements of an array, in both directions.</summary>
internal static class Elements
{
    /// <summary>Writes each element as a framed value of its declared type.</summary>
    public static void Write<T>(ref WireWriter writer, ReadOnlySpan<T> elements)
    {
        var codec = FormatterCache<T>.Instance;
        foreach (var element in elements)
            codec.Write(ref writer, element);
    }

    /// <summary>
    /// Reads <paramref name="count"/> framed elements into an array of exactly that length. When the
    /// bytes that remain could hold the array itself, it is allocated at its final length and read
    /// into; otherwise the elements are gathered in a pooled buffer as they arrive and copied once
    /// into the final array, so a count the payload does not back costs memory only for elements that
    /// actually arrived.
    /// </summary>
    public static T[] ReadArray<T>(ref WireReader reader, ElementCount count)
    {
        if (count.Value == 0)
            return [];

        var codec = FormatterCache<T>.Instance;
        var trace = reader.State.Trace;

        if (count.IsBackedBy(Unsafe.SizeOf<T>(), reader.Remaining))
        {
            var array = new T[count.Value];
            for (int i = 0; i < array.Length; i++)
            {
                trace?.LabelIndex(i);
                array[i] = codec.Read(ref reader);
            }

            return array;
        }

        var buffer = new ElementBuffer<T>(count.CapacityHint);
        try
        {
            for (int i = 0; i < count.Value; i++)
            {
                trace?.LabelIndex(i);
                buffer.Add(codec.Read(ref reader));
            }

            return buffer.ToArray();
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>The configured maximum for a count of <paramref name="kind"/>.</summary>
    public static long MaximumFor(ref OperationState state, CountKind kind) => kind switch
    {
        CountKind.Array => state.Limits.MaxArrayLength,
        CountKind.Collection => state.Limits.MaxCollectionLength,
        CountKind.Dictionary => state.Limits.MaxDictionaryEntries,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static BinaryFormatException CountChanged(string what, Type declaredType) =>
        new($"{what} changed while writing '{declaredType}'.");

    public static BinaryLimitException TooMany(string what, long maximum) =>
        new($"{what} exceeds the configured maximum of {maximum}.");

    /// <summary>
    /// Classifies a container's refusal of a value that came off the wire. The container raises the
    /// framework's own argument exception — for a duplicate key, a duplicate entry or a null key —
    /// and that is a statement about the payload, not about the caller's arguments, so it is a
    /// malformed payload here and never leaves the taxonomy.
    /// </summary>
    public static BinaryFormatException Refused(string what, Type declaredType, ArgumentException cause) =>
        new($"{what}: '{declaredType}' refused a value from the payload — a duplicate key, a " +
            "duplicate entry or a null key is not admitted.", cause);

    /// <summary>
    /// Requires the container to hold exactly what the payload declared. A container that collapses
    /// duplicates silently — a set, a concurrent dictionary, a frozen collection — reports fewer
    /// elements than were read, and that difference is the only evidence that the payload carried a
    /// duplicate at all.
    /// </summary>
    public static void RequireMaterialized(int? actual, int declared, string what, Type declaredType)
    {
        if (actual is { } materialized && materialized != declared)
            throw new BinaryFormatException(
                $"{what} declares {declared}, but '{declaredType}' materialized {materialized} — a " +
                "duplicate key or element is not admitted.");
    }
}
