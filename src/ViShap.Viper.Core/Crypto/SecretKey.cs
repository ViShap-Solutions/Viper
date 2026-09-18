using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// Owned key material. A <see cref="SecretKey"/> always holds its own copy of the bytes, so disposing
/// it can never clear memory that belongs to somebody else.
/// </summary>
/// <remarks>
/// <para>
/// This is the only kind of buffer Viper zeroes. Arrays you pass to the options builder, and arrays a
/// key resolver returns, are copied and left untouched — clearing them, or not, stays your decision.
/// </para>
/// <para>
/// An <see cref="IKeyProvider"/> hands out a fresh key per resolution, and the code that requested it
/// disposes it when the phase ends. <see cref="Span"/> is valid only until <see cref="Dispose"/> and
/// must never be stored.
/// </para>
/// <example>
/// <code>
/// using var key = SecretKey.CopyFrom(material);
/// // material is unchanged and still yours
/// </code>
/// </example>
/// </remarks>
public sealed class SecretKey : IDisposable
{
    private byte[] _material;
    private bool _disposed;

    private SecretKey(byte[] material) => _material = material;

    /// <summary>Creates an owned copy of <paramref name="material"/>.</summary>
    /// <param name="material">Key bytes to copy. The source is not modified.</param>
    /// <returns>A key that owns its own copy of the bytes.</returns>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException"><paramref name="material"/> is empty.</exception>
    public static SecretKey CopyFrom(ReadOnlySpan<byte> material)
    {
        if (material.IsEmpty)
            throw new BinaryEncryptionKeyException(
                "Key material is empty — an encryption key must contain at least one byte.");

        return new SecretKey(material.ToArray());
    }

    /// <summary>Length of the key in bytes.</summary>
    public int Length => _material.Length;

    /// <summary>
    /// The key bytes. Valid only until <see cref="Dispose"/>; never store the span.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The key has been disposed.</exception>
    public ReadOnlySpan<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _material;
        }
    }

    /// <summary>
    /// Clears the key material. Safe to call more than once; it only ever clears this instance's copy.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CryptographicOperations.ZeroMemory(_material);
        _material = [];
    }
}
