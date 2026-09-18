using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// AES-256 in GCM mode: confidentiality and authentication in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Requires a 256-bit (32-byte) key. The output is <c>nonce (12 bytes) || ciphertext || tag (16
/// bytes)</c>; the nonce is generated per message with a cryptographic RNG, so the same key may be
/// reused across messages without the caller tracking anything.
/// </para>
/// <para>
/// It authenticates the payload's format metadata as associated data, which makes the header
/// unforgeable: changing any algorithm identifier, key id, length or flag in it makes decryption fail
/// with <see cref="BinaryIntegrityException"/> rather than silently changing how the payload is read.
/// </para>
/// <para>
/// Authentication cannot distinguish a wrong key from modified data, so both surface as
/// <see cref="BinaryIntegrityException"/>. Key material is supplied through an
/// <see cref="IKeyProvider"/> and is never retained by this type.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithEncryption(new Aes256Gcm(), key, keyId: "2026-q3")
///     .RequireEncryption()
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class Aes256Gcm : IEncryptionAlgorithm
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <inheritdoc />
    public EncryptionAlgorithm Kind => EncryptionAlgorithm.Aes256Gcm;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public bool AuthenticatesAssociatedData => true;

    /// <inheritdoc />
    public int GetMaxCiphertextLength(int plaintextLength) =>
        NonceSizeBytes + plaintextLength + TagSizeBytes;

    /// <inheritdoc />
    public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination) =>
        Encrypt(plaintext, key, ReadOnlySpan<byte>.Empty, destination);

    /// <inheritdoc />
    public int Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        ValidateKey(key);

        int required = GetMaxCiphertextLength(plaintext.Length);
        if (destination.Length < required)
            throw new InvalidOperationException(
                $"Destination buffer too small for AES-GCM output. Need {required}, got {destination.Length}.");

        var nonce = destination[..NonceSizeBytes];
        var ciphertext = destination.Slice(NonceSizeBytes, plaintext.Length);
        var tag = destination.Slice(NonceSizeBytes + plaintext.Length, TagSizeBytes);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return required;
    }

    /// <inheritdoc />
    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, Span<byte> destination) =>
        Decrypt(ciphertext, key, ReadOnlySpan<byte>.Empty, destination);

    /// <inheritdoc />
    public int Decrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        ValidateKey(key);

        if (ciphertext.Length < NonceSizeBytes + TagSizeBytes)
            throw new BinaryFormatException("Ciphertext is too short for AES-GCM.");

        int plaintextLength = ciphertext.Length - NonceSizeBytes - TagSizeBytes;
        if (destination.Length < plaintextLength)
            throw new BinaryFormatException(
                $"Destination buffer too small for decrypted output. Need {plaintextLength}, " +
                $"got {destination.Length}.");

        var nonce = ciphertext[..NonceSizeBytes];
        var payload = ciphertext.Slice(NonceSizeBytes, plaintextLength);
        var tag = ciphertext.Slice(NonceSizeBytes + plaintextLength, TagSizeBytes);

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, payload, tag, destination[..plaintextLength], associatedData);
            return plaintextLength;
        }
        catch (CryptographicException ex)
        {
            throw new BinaryIntegrityException(
                "Decryption failed: wrong key, tampered payload, or tampered format metadata.", ex);
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
            throw new BinaryEncryptionKeyException(
                $"Aes256Gcm requires a {KeySizeBytes}-byte (256-bit) key, got {key.Length}.");
    }
}
