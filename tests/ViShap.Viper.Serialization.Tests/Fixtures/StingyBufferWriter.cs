using System.Buffers;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> that hands out spans of at most <paramref name="limit"/> bytes,
/// whatever it is asked for. A positive limit is a legitimate writer that grows in small pieces; a
/// limit of zero hands out an empty span, which the <see cref="IBufferWriter{T}"/> contract forbids.
/// </summary>
internal sealed class StingyBufferWriter(int limit) : IBufferWriter<byte>
{
    private readonly List<byte> _written = [];
    private byte[] _current = [];

    /// <summary>How many spans were asked for.</summary>
    public int Requests { get; private set; }

    /// <summary>The bytes committed so far, in order.</summary>
    public byte[] Written => [.. _written];

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Requests++;
        _current = new byte[limit];
        return _current;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Requests++;
        _current = new byte[limit];
        return _current;
    }

    public void Advance(int count)
    {
        if ((uint)count > (uint)_current.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        _written.AddRange(_current.AsSpan(0, count).ToArray());
        _current = [];
    }
}
