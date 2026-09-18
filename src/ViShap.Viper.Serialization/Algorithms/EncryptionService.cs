using System.Buffers;
using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// Runs an encryption algorithm over a payload, binding the format metadata as associated data.
/// Key material is always an owned <see cref="SecretKey"/> obtained from the configured provider and
/// disposed here, so the serializer never clears memory it does not own.
/// </summary>
internal sealed class EncryptionService(IEncryptionAlgorithm algorithm, string? keyId)
{
    public EncryptionAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;
    public string? KeyId => keyId;
    public bool AuthenticatesAssociatedData => algorithm.AuthenticatesAssociatedData;

    public byte[] Encrypt(
        byte[] plaintext,
        ReadOnlySpan<byte> associatedData,
        IKeyProvider? keys,
        long maxEncryptedBytes)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (algorithm.Kind == EncryptionAlgorithm.None)
            return plaintext;

        using var key = Resolve(keys, keyId);

        int maxLength = algorithm.GetMaxCiphertextLength(plaintext.Length);
        if (maxLength < 0)
            throw new BinaryConfigurationException(
                "The encryption algorithm returned a negative maximum ciphertext length.");

        int destinationLength = (int)Math.Min(maxLength, maxEncryptedBytes);
        bool capped = destinationLength < maxLength;

        byte[] rented = ArrayPool<byte>.Shared.Rent(destinationLength);
        try
        {
            int written;
            try
            {
                written = algorithm.Encrypt(
                    plaintext, key.Span, associatedData, rented.AsSpan(0, destinationLength));
            }
            catch (Exception ex) when (capped && ex is InvalidOperationException or BinaryFormatException)
            {
                throw new BinaryLimitException(
                    $"Encrypted payload could not fit within the configured maximum of " +
                    $"{maxEncryptedBytes} bytes.", ex);
            }
            catch (CryptographicException ex)
            {
                throw new BinaryEncryptionException(
                    "Encryption failed.", ex);
            }

            if (written < 0 || written > destinationLength)
                throw new BinaryConfigurationException(
                    $"The encryption algorithm returned an invalid output length of {written}.");

            return rented.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    public static byte[] Decrypt(
        IEncryptionAlgorithm algorithm,
        byte[] ciphertext,
        ReadOnlySpan<byte> associatedData,
        IKeyProvider? keys,
        string? headerKeyId,
        int expectedPlaintextLength)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (algorithm.Kind == EncryptionAlgorithm.None)
        {
            if (ciphertext.Length != expectedPlaintextLength)
                throw new BinaryFormatException(
                    $"Ciphertext length {ciphertext.Length} does not match the declared plaintext " +
                    $"length {expectedPlaintextLength} when encryption is None.");

            return ciphertext;
        }

        // Decryption never expands: the declared plaintext cannot exceed the ciphertext that was
        // actually delivered, so a short frame cannot force a large allocation by claiming one.
        if (expectedPlaintextLength > ciphertext.Length)
            throw new BinaryFormatException(
                $"Declared plaintext length {expectedPlaintextLength} exceeds the {ciphertext.Length} " +
                "ciphertext byte(s) present.");

        using var key = Resolve(keys, headerKeyId);

        var result = new byte[expectedPlaintextLength];
        int written;
        try
        {
            written = algorithm.Decrypt(ciphertext, key.Span, associatedData, result);
        }
        catch (BinarySerializerException)
        {
            Array.Clear(result);
            throw;
        }
        catch (CryptographicException ex)
        {
            Array.Clear(result);
            throw new BinaryIntegrityException(
                "Decryption failed: wrong key, tampered payload, or tampered format metadata.", ex);
        }

        if (written != expectedPlaintextLength)
        {
            Array.Clear(result);
            throw new BinaryFormatException(
                $"Decryption produced {written} bytes, expected {expectedPlaintextLength}.");
        }

        return result;
    }

    private static SecretKey Resolve(IKeyProvider? keys, string? keyId) =>
        keys is null
            ? throw new BinaryEncryptionKeyException(
                $"Encrypted payload requires key material for key id '{keyId ?? "(none)"}', " +
                "but no key provider is configured.")
            : keys.Resolve(keyId);
}
