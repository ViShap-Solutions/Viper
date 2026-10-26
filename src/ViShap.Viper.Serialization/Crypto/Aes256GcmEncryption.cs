using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// AES-256 in GCM mode: confidentiality and authentication in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Requires a 256-bit (32-byte) key. The output is <c>nonce (12 bytes) || ciphertext || tag (16
/// bytes)</c>, 28 bytes longer than the plaintext; the nonce is generated per message with a
/// cryptographic RNG, so the same key may be reused across messages without the caller tracking
/// anything.
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
///     .WithEncryption(new Aes256GcmEncryption(), key, keyId: "2026-q3")
///     .RequireEncryption()
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class Aes256GcmEncryption : IEncryptionAlgorithm
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <inheritdoc />
    public EncryptionAlgorithm Kind => EncryptionAlgorithm.Aes256Gcm;

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
    public int Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        Aead.ValidateKey(key, KeySizeInBytes, nameof(Aes256GcmEncryption));

        int length = GetCiphertextLength(plaintext.Length);
        Aead.CheckDestination(destination, length);

        var nonce = destination[..NonceSizeBytes];
        var ciphertext = destination.Slice(NonceSizeBytes, plaintext.Length);
        var tag = destination.Slice(NonceSizeBytes + plaintext.Length, TagSizeBytes);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return length;
    }

    /// <inheritdoc />
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="key"/> is not 32 bytes long.</exception>
    /// <exception cref="BinaryFormatException"><paramref name="ciphertext"/> is shorter than a nonce and a tag.</exception>
    public int Decrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        Aead.ValidateKey(key, KeySizeInBytes, nameof(Aes256GcmEncryption));

        int plaintextLength = Aead.PlaintextLength(
            ciphertext, destination, NonceSizeBytes + TagSizeBytes, "AES-GCM");

        var nonce = ciphertext[..NonceSizeBytes];
        var payload = ciphertext.Slice(NonceSizeBytes, plaintextLength);
        var tag = ciphertext.Slice(NonceSizeBytes + plaintextLength, TagSizeBytes);
        var plaintext = destination[..plaintextLength];

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, payload, tag, plaintext, associatedData);
            return plaintext.Length;
        }
        catch (CryptographicException ex)
        {
            throw Aead.AuthenticationFailed(ex);
        }
    }
}
