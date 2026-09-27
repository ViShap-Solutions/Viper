using System.Buffers;

namespace ViShap.Viper.Io;

/// <summary>
/// The output of one pipeline phase: an array rented from <see cref="ArrayPool{T}.Shared"/> and the
/// number of bytes of it that hold the result. Disposing it clears the array and returns it to the
/// pool, because what it holds is payload, plaintext or ciphertext; it is disposed exactly once, by
/// its owner. The default value owns nothing.
/// </summary>
internal readonly struct RentedBytes : IDisposable
{
    private readonly byte[]? _array;

    private RentedBytes(byte[] array, int length)
    {
        _array = array;
        Length = length;
    }

    /// <summary>Rents an array of at least <paramref name="minimumLength"/> bytes.</summary>
    public static byte[] RentArray(int minimumLength) => ArrayPool<byte>.Shared.Rent(minimumLength);

    /// <summary>Clears an array taken from <see cref="RentArray"/> and returns it to the pool.</summary>
    public static void ReturnArray(byte[] array) => ArrayPool<byte>.Shared.Return(array, clearArray: true);

    /// <summary>
    /// Takes ownership of <paramref name="array"/>, obtained from <see cref="RentArray"/>, whose first
    /// <paramref name="length"/> bytes hold the result.
    /// </summary>
    public static RentedBytes Adopt(byte[] array, int length)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)length, (uint)array.Length, nameof(length));

        return new RentedBytes(array, length);
    }

    /// <summary>The number of bytes that hold the result.</summary>
    public int Length { get; }

    /// <summary>The result.</summary>
    public ReadOnlySpan<byte> Span => _array.AsSpan(0, Length);

    /// <summary>Clears the array and returns it to the pool.</summary>
    public void Dispose()
    {
        if (_array is not null)
            ReturnArray(_array);
    }
}
