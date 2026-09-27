using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// Runs an encryption algorithm over a payload, binding the format metadata as associated data.
/// Key material is always an owned <see cref="SecretKey"/> obtained from the configured provider and
/// disposed here, so the serializer never clears memory it does not own. Every result is a pooled
/// buffer owned by the caller.
/// </summary>
internal sealed class EncryptionService(IEncryptionAlgorithm algorithm, string? keyId)
{
    public EncryptionAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;
    public string? KeyId => keyId;
    public bool AuthenticatesAssociatedData => algorithm.AuthenticatesAssociatedData;

    /// <summary>Encrypts <paramref name="plaintext"/>. Not called for <see cref="EncryptionAlgorithm.None"/>.</summary>
    public RentedBytes Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData,
        IKeyProvider? keys,
        long maxEncryptedBytes)
    {
        using var key = Resolve(keys, keyId);

        int maxLength = algorithm.GetMaxCiphertextLength(plaintext.Length);
        if (maxLength < 0)
            throw new BinaryConfigurationException(
                "The encryption algorithm returned a negative maximum ciphertext length.");

        int destinationLength = (int)Math.Min(maxLength, maxEncryptedBytes);
        bool capped = destinationLength < maxLength;

        byte[] rented = RentedBytes.RentArray(destinationLength);
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
                    $"{maxEncryptedBytes} bytes " +
                    $"({nameof(SerializationLimits.MaxEncryptedBytes)}).", ex);
            }
            catch (CryptographicException ex)
            {
                throw new BinaryEncryptionException(
                    "Encryption failed.", ex);
            }

            if (written < 0 || written > destinationLength)
                throw new BinaryConfigurationException(
                    $"The encryption algorithm returned an invalid output length of {written}.");

            return RentedBytes.Adopt(rented, written);
        }
        catch
        {
            RentedBytes.ReturnArray(rented);
            throw;
        }
    }

    /// <summary>
    /// Checks a payload stored without encryption: its length is the declared plaintext length.
    /// </summary>
    public static void RequireStored(ReadOnlySpan<byte> ciphertext, int expectedPlaintextLength)
    {
        if (ciphertext.Length != expectedPlaintextLength)
            throw new BinaryFormatException(
                $"Ciphertext length {ciphertext.Length} does not match the declared plaintext " +
                $"length {expectedPlaintextLength} when encryption is None.");
    }

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/> into exactly the declared plaintext length. Not called
    /// for <see cref="EncryptionAlgorithm.None"/>; see <see cref="RequireStored"/>.
    /// </summary>
    public static RentedBytes Decrypt(
        IEncryptionAlgorithm algorithm,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> associatedData,
        IKeyProvider? keys,
        string? headerKeyId,
        int expectedPlaintextLength)
    {
        // Decryption never expands: the declared plaintext cannot exceed the ciphertext that was
        // actually delivered, so a short frame cannot force a large allocation by claiming one.
        if (expectedPlaintextLength > ciphertext.Length)
            throw new BinaryFormatException(
                $"Declared plaintext length {expectedPlaintextLength} exceeds the {ciphertext.Length} " +
                "ciphertext byte(s) present.");

        using var key = Resolve(keys, headerKeyId);

        byte[] rented = RentedBytes.RentArray(expectedPlaintextLength);
        try
        {
            int written;
            try
            {
                written = algorithm.Decrypt(
                    ciphertext, key.Span, associatedData, rented.AsSpan(0, expectedPlaintextLength));
            }
            catch (CryptographicException ex)
            {
                throw new BinaryIntegrityException(
                    "Decryption failed: wrong key, tampered payload, or tampered format metadata.", ex);
            }

            if (written != expectedPlaintextLength)
                throw new BinaryFormatException(
                    $"Decryption produced {written} bytes, expected {expectedPlaintextLength}.");

            return RentedBytes.Adopt(rented, written);
        }
        catch
        {
            RentedBytes.ReturnArray(rented);
            throw;
        }
    }

    private static SecretKey Resolve(IKeyProvider? keys, string? keyId) =>
        keys is null
            ? throw new BinaryEncryptionKeyException(
                $"Encrypted payload requires key material for key id '{keyId ?? "(none)"}', " +
                "but no key provider is configured.")
            : keys.Resolve(keyId);
}
