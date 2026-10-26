namespace ViShap.Viper.Pipeline;

/// <summary>
/// The bytes of one frame gathered from a source that delivers them over time, held in an array
/// rented from the pool. It grows only as bytes arrive: a length the source is known to hold is
/// allocated at once, and one that is only declared doubles the buffer at each step, so what a frame
/// costs follows what it delivered, never what it declared. Disposing it clears the array and returns
/// it to the pool.
/// </summary>
internal struct FrameBuffer : IDisposable
{
    private const int FirstBytes = 256;

    private byte[]? _array;

    /// <summary>The bytes gathered so far.</summary>
    public int Length { get; private set; }

    /// <summary>The bytes gathered so far.</summary>
    public readonly ReadOnlySpan<byte> Span => _array.AsSpan(0, Length);

    /// <summary>
    /// The space the next read fills, on the way to <paramref name="target"/> bytes: never past it, so
    /// a source is never asked for a byte beyond what the frame needs.
    /// </summary>
    /// <param name="target">The bytes the frame needs, counted from its start.</param>
    /// <param name="backed">Whether the source is known to hold them, so they are allocated at once.</param>
    public Memory<byte> Free(int target, bool backed)
    {
        int capacity = _array?.Length ?? 0;
        if (capacity - Length <= 0 || (backed && capacity < target))
            Grow(backed ? target : (int)Math.Min(target, Math.Max(FirstBytes, 2L * capacity)));

        return _array.AsMemory(Length, Math.Min(_array!.Length, target) - Length);
    }

    /// <summary>Counts <paramref name="count"/> bytes a read placed in the space <see cref="Free"/> returned.</summary>
    public void Advance(int count) => Length += count;

    /// <summary>Clears the array and returns it to the pool.</summary>
    public void Dispose()
    {
        if (_array is not null)
            RentedBytes.ReturnArray(_array);

        _array = null;
        Length = 0;
    }

    private void Grow(int capacity)
    {
        byte[] grown = RentedBytes.RentArray(capacity);
        if (_array is not null)
        {
            Span.CopyTo(grown);
            RentedBytes.ReturnArray(_array);
        }

        _array = grown;
    }
}
