using System.Buffers;

namespace ViShap.Viper;

/// <summary>
/// An encoded payload held in an array rented from the shared pool, returned by
/// <see cref="BinarySerializer.SerializePooled{T}(T)"/>. Use it to hand the bytes to a socket, a pipe
/// or a queue without allocating a new array for every message.
/// </summary>
/// <remarks>
/// <para>
/// The instance owns the array. <see cref="Memory"/> and <see cref="Span"/> are valid until
/// <see cref="Dispose"/>, which clears the bytes and returns the array to the pool; after that, the
/// memory may belong to another message, so keep no reference to it. Dispose the payload exactly
/// where the bytes are no longer needed — a <see langword="using"/> declaration is the usual way.
/// </para>
/// <para>
/// Disposing more than once is safe. Reading <see cref="Memory"/> or <see cref="Span"/> after
/// disposing raises <see cref="ObjectDisposedException"/>.
/// </para>
/// <example>
/// <code>
/// using PooledPayload payload = serializer.SerializePooled(order);
/// await socket.SendAsync(payload.Memory, cancellationToken);
/// </code>
/// </example>
/// </remarks>
public sealed class PooledPayload : IDisposable
{
    private byte[]? _array;
    private readonly int _length;

    internal PooledPayload(byte[] array, int length)
    {
        _array = array;
        _length = length;
    }

    /// <summary>The encoded bytes.</summary>
    /// <exception cref="ObjectDisposedException">The payload has been disposed.</exception>
    public ReadOnlyMemory<byte> Memory => Owned().AsMemory(0, _length);

    /// <summary>The encoded bytes.</summary>
    /// <exception cref="ObjectDisposedException">The payload has been disposed.</exception>
    public ReadOnlySpan<byte> Span => Owned().AsSpan(0, _length);

    /// <summary>Clears the bytes and returns the array to the pool. Later calls do nothing.</summary>
    public void Dispose()
    {
        byte[]? array = Interlocked.Exchange(ref _array, null);
        if (array is not null)
            ArrayPool<byte>.Shared.Return(array, clearArray: true);
    }

    private byte[] Owned() => _array ?? throw new ObjectDisposedException(nameof(PooledPayload));
}
