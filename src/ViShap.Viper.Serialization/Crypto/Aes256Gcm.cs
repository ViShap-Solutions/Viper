using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

public sealed class Aes256Gcm : IEncryptionAlgorithm
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public EncryptionAlgorithm Kind => EncryptionAlgorithm.Aes256Gcm;
    public string? CustomName => null;
    public int GetMaxCiphertextLength(int plaintextLength) => NonceSizeBytes + plaintextLength + TagSizeBytes;

    public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination)
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
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return NonceSizeBytes + plaintext.Length + TagSizeBytes;
    }

    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, Span<byte> destination)
    {
        ValidateKey(key);

        if (ciphertext.Length < NonceSizeBytes + TagSizeBytes)
            throw new BinaryFormatException("Ciphertext is too short for AES-GCM.");

        int plaintextLength = ciphertext.Length - NonceSizeBytes - TagSizeBytes;
        if (destination.Length < plaintextLength)
            throw new BinaryFormatException($"Destination buffer too small for decrypted output. Need {plaintextLength}, got {destination.Length}.");

        var nonce = ciphertext[..NonceSizeBytes];
        var encryptedPayload = ciphertext.Slice(NonceSizeBytes, plaintextLength);
        var tag = ciphertext.Slice(NonceSizeBytes + plaintextLength, TagSizeBytes);

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, encryptedPayload, tag, destination[..plaintextLength]);
            return plaintextLength;
        }
        catch (CryptographicException ex)
        {
            throw new BinaryIntegrityException("Decryption failed: wrong key or tampered payload.", ex);
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Aes256Gcm requires a {KeySizeBytes}-byte (256-bit) key, got {key.Length}.", nameof(key));
    }
}