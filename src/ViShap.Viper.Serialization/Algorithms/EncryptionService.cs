using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// Runs an encryption algorithm over a payload, binding the format metadata as associated data, and
/// holds the algorithm to what it declares: the ciphertext length it states, the key size it
/// requires, a destination filled exactly, a plaintext no longer than its ciphertext. Key material is
/// always an owned <see cref="SecretKey"/> obtained from the configured provider and disposed by the
/// code that resolved it, so the serializer never clears memory it does not own.
/// </summary>
internal sealed class EncryptionService(IEncryptionAlgorithm algorithm, string? keyId)
{
    public IEncryptionAlgorithm Algorithm => algorithm;

    public EncryptionAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;
    public string? KeyId => keyId;

    /// <summary>
    /// The exact length of the ciphertext for <paramref name="plaintextLength"/> bytes, as the
    /// algorithm states it, which sizes the frame before anything is encrypted.
    /// </summary>
    /// <exception cref="BinaryConfigurationException">The stated length is negative or shorter than the plaintext.</exception>
    public int CiphertextLength(int plaintextLength)
    {
        int length = algorithm.GetCiphertextLength(plaintextLength);
        if (length < plaintextLength)
            throw new BinaryConfigurationException(
                $"Encryption algorithm '{Name(algorithm)}' states a ciphertext of {length} byte(s) for " +
                $"{plaintextLength} byte(s) of plaintext; a ciphertext is never shorter than its plaintext.");

        return length;
    }

    /// <summary>Resolves the key the payload is written under.</summary>
    /// <exception cref="BinaryEncryptionKeyException">No provider, no key, or a key of the wrong size.</exception>
    public SecretKey ResolveKey(IKeyProvider? keys) => Resolve(algorithm, keys, keyId);

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into <paramref name="destination"/>, which is exactly the
    /// stated ciphertext length and must be filled exactly.
    /// </summary>
    /// <exception cref="BinaryConfigurationException">The algorithm wrote other than the stated length, or refused the destination.</exception>
    /// <exception cref="BinaryEncryptionException">The cipher failed.</exception>
    public static void Seal(
        IEncryptionAlgorithm algorithm,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        int written;
        try
        {
            written = algorithm.Encrypt(plaintext, key, associatedData, destination);
        }
        catch (CryptographicException ex)
        {
            throw new BinaryEncryptionException("Encryption failed.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new BinaryConfigurationException(
                $"Encryption algorithm '{Name(algorithm)}' refused a destination of the " +
                $"{destination.Length} byte(s) it stated for its ciphertext.", ex);
        }

        if (written != destination.Length)
            throw new BinaryConfigurationException(
                $"Encryption algorithm '{Name(algorithm)}' wrote {written} byte(s) into a destination " +
                $"of the {destination.Length} byte(s) it stated for its ciphertext.");
    }

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/> into a pooled buffer as long as the ciphertext, so the
    /// memory a frame costs follows the bytes it delivered: the plaintext length is not declared, and
    /// is known only from what the algorithm reports. Not called for
    /// <see cref="EncryptionAlgorithm.None"/>.
    /// </summary>
    public static RentedBytes Decrypt(
        IEncryptionAlgorithm algorithm,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> associatedData,
        IKeyProvider? keys,
        string? headerKeyId)
    {
        using var key = Resolve(algorithm, keys, headerKeyId);

        byte[] rented = RentedBytes.RentArray(ciphertext.Length);
        try
        {
            int written;
            try
            {
                written = algorithm.Decrypt(
                    ciphertext, key.Span, associatedData, rented.AsSpan(0, ciphertext.Length));
            }
            catch (CryptographicException ex)
            {
                throw new BinaryIntegrityException(
                    "Decryption failed: wrong key, tampered payload, or tampered format metadata.", ex);
            }
            catch (ArgumentException ex)
            {
                throw new BinaryConfigurationException(
                    $"Encryption algorithm '{Name(algorithm)}' refused a destination as long as the " +
                    $"{ciphertext.Length}-byte ciphertext.", ex);
            }

            if (written < 0 || written > ciphertext.Length)
                throw new BinaryConfigurationException(
                    $"Encryption algorithm '{Name(algorithm)}' reported {written} plaintext byte(s) " +
                    $"from a {ciphertext.Length}-byte ciphertext.");

            return RentedBytes.Adopt(rented, written);
        }
        catch
        {
            RentedBytes.ReturnArray(rented);
            throw;
        }
    }

    private static SecretKey Resolve(IEncryptionAlgorithm algorithm, IKeyProvider? keys, string? keyId)
    {
        if (keys is null)
            throw new BinaryEncryptionKeyException(
                $"Encrypted payload requires key material for key id '{keyId ?? "(none)"}', " +
                "but no key provider is configured.");

        var key = keys.Resolve(keyId)
                  ?? throw new BinaryEncryptionKeyException(
                      $"The key provider returned no key for key id '{keyId ?? "(none)"}'.");

        if (key.Length != algorithm.KeySizeInBytes)
        {
            int length = key.Length;
            key.Dispose();

            throw new BinaryEncryptionKeyException(
                $"Encryption algorithm '{Name(algorithm)}' requires a {algorithm.KeySizeInBytes}-byte " +
                $"key, but the key resolved for key id '{keyId ?? "(none)"}' is {length} byte(s).");
        }

        return key;
    }

    private static string Name(IEncryptionAlgorithm algorithm) =>
        algorithm.CustomName ?? algorithm.Kind.ToString();
}
