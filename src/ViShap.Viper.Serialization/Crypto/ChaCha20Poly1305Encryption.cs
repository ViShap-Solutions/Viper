using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// ChaCha20-Poly1305 (RFC 8439): confidentiality and authentication in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Requires a 256-bit (32-byte) key. The output is <c>nonce (12 bytes) || ciphertext || tag (16
/// bytes)</c>, 28 bytes longer than the plaintext; the nonce is generated per message with a
/// cryptographic RNG, so the same key may be reused across messages without the caller tracking
/// anything. It is implemented in software, so it does not depend on processor AES instructions, which
/// <see cref="Aes256GcmEncryption"/> uses where they exist.
/// </para>
/// <para>
/// It authenticates the payload's format metadata as associated data, exactly as
/// <see cref="Aes256GcmEncryption"/> does, and a wrong key or modified data both surface as
/// <see cref="BinaryIntegrityException"/>.
/// </para>
/// <para>
/// The algorithm comes from the platform's cryptography library, and not every platform provides it.
/// Where it is missing, building options that encrypt with it, and reading a payload encrypted with
/// it, both fail with <see cref="BinaryFormatNotSupportedException"/>.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithEncryption(new ChaCha20Poly1305Encryption(), key, keyId: "2026-q3")
///     .RequireEncryption()
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class ChaCha20Poly1305Encryption : IEncryptionAlgorithm
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <inheritdoc />
    public EncryptionAlgorithm Kind => EncryptionAlgorithm.ChaCha20Poly1305;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public bool AuthenticatesAssociatedData => true;

    /// <inheritdoc />
    /// <remarks>32 bytes.</remarks>
    public int KeySizeInBytes => 32;

    /// <inheritdoc />
    /// <remarks>The plaintext length plus 28: a 12-byte nonce and a 16-byte tag.</remarks>
    public int GetCiphertextLength(int plaintextLength) =>
        checked(NonceSizeBytes + plaintextLength + TagSizeBytes);

    /// <inheritdoc />
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="key"/> is not 32 bytes long.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">The platform does not provide ChaCha20-Poly1305.</exception>
    public int Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        EnsureSupported();
        Aead.ValidateKey(key, KeySizeInBytes, nameof(ChaCha20Poly1305Encryption));

        int length = GetCiphertextLength(plaintext.Length);
        Aead.CheckDestination(destination, length);

        var nonce = destination[..NonceSizeBytes];
        var ciphertext = destination.Slice(NonceSizeBytes, plaintext.Length);
        var tag = destination.Slice(NonceSizeBytes + plaintext.Length, TagSizeBytes);

        RandomNumberGenerator.Fill(nonce);

        using var cipher = new ChaCha20Poly1305(key);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return length;
    }

    /// <inheritdoc />
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="key"/> is not 32 bytes long.</exception>
    /// <exception cref="BinaryFormatException"><paramref name="ciphertext"/> is shorter than a nonce and a tag.</exception>
    /// <exception cref="BinaryFormatNotSupportedException">The platform does not provide ChaCha20-Poly1305.</exception>
    public int Decrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        EnsureSupported();
        Aead.ValidateKey(key, KeySizeInBytes, nameof(ChaCha20Poly1305Encryption));

        int plaintextLength = Aead.PlaintextLength(
            ciphertext, destination, NonceSizeBytes + TagSizeBytes, "ChaCha20-Poly1305");

        var nonce = ciphertext[..NonceSizeBytes];
        var payload = ciphertext.Slice(NonceSizeBytes, plaintextLength);
        var tag = ciphertext.Slice(NonceSizeBytes + plaintextLength, TagSizeBytes);
        var plaintext = destination[..plaintextLength];

        try
        {
            using var cipher = new ChaCha20Poly1305(key);
            cipher.Decrypt(nonce, payload, tag, plaintext, associatedData);
            return plaintext.Length;
        }
        catch (CryptographicException ex)
        {
            throw Aead.AuthenticationFailed(ex);
        }
    }

    /// <summary>
    /// Refuses the algorithm where the platform's cryptography library does not provide it: when
    /// options that encrypt with it are built, when a payload that names it is read, and in the
    /// primitive itself.
    /// </summary>
    /// <exception cref="BinaryFormatNotSupportedException">The platform does not provide ChaCha20-Poly1305.</exception>
    internal static void EnsureSupported()
    {
        if (!ChaCha20Poly1305.IsSupported)
            throw new BinaryFormatNotSupportedException(
                $"'{nameof(ChaCha20Poly1305Encryption)}' is not supported on this platform: its " +
                "cryptography library does not provide ChaCha20-Poly1305.");
    }
}
