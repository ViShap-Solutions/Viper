using System.Buffers;

namespace ViShap.Viper.Io;

/// <summary>
/// The serializer's own write buffer for one operation: a chain of segments rented from
/// <see cref="ArrayPool{T}.Shared"/>, which grows without copying what it already holds and lets an
/// earlier position be patched in place, which is what a keyed field's length needs once the field
/// has been written.
/// <para>
/// The buffer enforces one byte budget. It never hands out space past it, so a graph that would
/// exceed the budget fails while it is being written, and nothing reaches a destination until the
/// pipeline copies the finished bytes out. Every segment is cleared before it goes back to the pool.
/// </para>
/// </summary>
internal sealed class PayloadBuffer : IDisposable
{
    private const int FirstSegmentBytes = 512;
    private const int LargestGrowthBytes = 1024 * 1024;

    private readonly long _maximum;
    private readonly string _resourceName;
    private readonly List<byte[]> _segments = [];
    private readonly List<int> _segmentLengths = [];
    private byte[]? _current;
    private int _currentLength;
    private long _completedLength;

    /// <param name="maximum">The largest number of bytes the buffer may hold.</param>
    /// <param name="resourceName">The budget named in a limit failure, e.g. "payload".</param>
    public PayloadBuffer(long maximum, string resourceName)
    {
        if (maximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        _maximum = maximum;
        _resourceName = resourceName;
    }

    /// <summary>The bytes committed so far.</summary>
    public long Length => _completedLength + _currentLength;

    /// <summary>
    /// Returns writable space of at least <paramref name="sizeHint"/> bytes, never extending past the
    /// budget. The bytes become part of the buffer only through <see cref="Advance"/>.
    /// </summary>
    /// <exception cref="BinaryLimitException">
    /// <paramref name="sizeHint"/> more bytes would exceed the budget.
    /// </exception>
    public Span<byte> GetSpan(int sizeHint)
    {
        long budgetLeft = _maximum - Length;
        if (sizeHint > budgetLeft)
            throw BudgetExceeded();

        if (_current is null || _current.Length - _currentLength < Math.Max(sizeHint, 1))
            StartSegment(Math.Max(sizeHint, 1));

        int free = _current!.Length - _currentLength;
        return _current.AsSpan(_currentLength, (int)Math.Min(free, budgetLeft));
    }

    /// <summary>Commits <paramref name="count"/> bytes written into the last span handed out.</summary>
    public void Advance(int count)
    {
        int free = _current is null ? 0 : _current.Length - _currentLength;
        if ((uint)count > (uint)free || Length + count > _maximum)
            throw new ArgumentOutOfRangeException(nameof(count));

        _currentLength += count;
    }

    /// <summary>Overwrites bytes already committed, starting at <paramref name="position"/>.</summary>
    public void Patch(long position, ReadOnlySpan<byte> bytes)
    {
        if (position < 0 || position + bytes.Length > Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        long segmentStart = 0;
        for (int index = 0; index <= _segments.Count && !bytes.IsEmpty; index++)
        {
            var (segment, length) = index < _segments.Count
                ? (_segments[index], _segmentLengths[index])
                : (_current!, _currentLength);

            long segmentEnd = segmentStart + length;
            if (position < segmentEnd)
            {
                int offset = (int)(position - segmentStart);
                int take = Math.Min(bytes.Length, length - offset);
                bytes[..take].CopyTo(segment.AsSpan(offset));
                bytes = bytes[take..];
                position += take;
            }

            segmentStart = segmentEnd;
        }
    }

    /// <summary>Copies the committed bytes into a new array.</summary>
    public byte[] ToArray()
    {
        var result = GC.AllocateUninitializedArray<byte>(checked((int)Length));
        CopyTo(result);
        return result;
    }

    /// <summary>Copies the committed bytes to <paramref name="destination"/>, in order.</summary>
    public void CopyTo(Span<byte> destination)
    {
        for (int index = 0; index < _segments.Count; index++)
        {
            _segments[index].AsSpan(0, _segmentLengths[index]).CopyTo(destination);
            destination = destination[_segmentLengths[index]..];
        }

        if (_current is not null)
            _current.AsSpan(0, _currentLength).CopyTo(destination);
    }

    /// <summary>Writes the committed bytes to <paramref name="destination"/>, segment by segment.</summary>
    public void WriteTo(Stream destination)
    {
        for (int index = 0; index < _segments.Count; index++)
            destination.Write(_segments[index].AsSpan(0, _segmentLengths[index]));

        if (_current is not null)
            destination.Write(_current.AsSpan(0, _currentLength));
    }

    /// <summary>Writes the committed bytes to <paramref name="destination"/>, segment by segment.</summary>
    public async ValueTask WriteToAsync(Stream destination, CancellationToken cancellationToken)
    {
        for (int index = 0; index < _segments.Count; index++)
            await destination.WriteAsync(
                _segments[index].AsMemory(0, _segmentLengths[index]), cancellationToken).ConfigureAwait(false);

        if (_current is not null)
            await destination.WriteAsync(_current.AsMemory(0, _currentLength), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Copies the committed bytes into <paramref name="destination"/>, segment by segment.</summary>
    public void WriteTo(IBufferWriter<byte> destination)
    {
        for (int index = 0; index < _segments.Count; index++)
            CopyInto(destination, _segments[index].AsSpan(0, _segmentLengths[index]));

        if (_current is not null)
            CopyInto(destination, _current.AsSpan(0, _currentLength));
    }

    /// <summary>
    /// Copies <paramref name="bytes"/> into <paramref name="destination"/>, in as many spans as it hands
    /// out, however short.
    /// </summary>
    /// <exception cref="BinaryStreamException">
    /// The writer handed out an empty span, which a buffer writer must not do; the copy would never end.
    /// </exception>
    internal static void CopyInto(IBufferWriter<byte> destination, ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            var span = destination.GetSpan(bytes.Length);
            if (span.IsEmpty)
                throw new BinaryStreamException(
                    "The destination buffer writer handed out an empty span, so the frame cannot be " +
                    "copied into it.");

            int take = Math.Min(span.Length, bytes.Length);
            bytes[..take].CopyTo(span);
            destination.Advance(take);
            bytes = bytes[take..];
        }
    }

    /// <summary>Clears every segment and returns it to the pool.</summary>
    public void Dispose()
    {
        foreach (var segment in _segments)
            ArrayPool<byte>.Shared.Return(segment, clearArray: true);

        if (_current is not null)
            ArrayPool<byte>.Shared.Return(_current, clearArray: true);

        _segments.Clear();
        _segmentLengths.Clear();
        _current = null;
        _currentLength = 0;
        _completedLength = 0;
    }

    private void StartSegment(int sizeHint)
    {
        if (_current is not null)
        {
            _segments.Add(_current);
            _segmentLengths.Add(_currentLength);
            _completedLength += _currentLength;
        }

        int growth = _current is null
            ? FirstSegmentBytes
            : (int)Math.Min(LargestGrowthBytes, (long)_current.Length * 2);

        _current = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, growth));
        _currentLength = 0;
    }

    private BinaryLimitException BudgetExceeded() =>
        new($"The {_resourceName} byte budget of {_maximum} would be exceeded.");
}
