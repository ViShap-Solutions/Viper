using System.Buffers;
using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

public sealed class Encryptor : IEncryptor, IDisposable
{
    private readonly IEncryptionAlgorithm _defaultAlgorithm;
    private readonly byte[]? _fixedKey;
    private readonly Func<string?, byte[]?>? _keyResolver;
    private readonly SerializationLimits _limits;
    private bool _disposed;

    public Encryptor(
        IEncryptionAlgorithm algorithm,
        byte[] key,
        string? keyId = null,
        SerializationLimits? limits = null)
    {
        _defaultAlgorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _fixedKey = key ?? throw new ArgumentNullException(nameof(key));
        DefaultKeyId = keyId;
        _limits = limits ?? SerializationLimits.Default;
        _limits.Validate();
    }

    public Encryptor(
        IEncryptionAlgorithm algorithm,
        Func<string?, byte[]?> keyResolver,
        string? keyId = null,
        SerializationLimits? limits = null)
    {
        _defaultAlgorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
        DefaultKeyId = keyId;
        _limits = limits ?? SerializationLimits.Default;
        _limits.Validate();
    }

    private Encryptor()
    {
        _defaultAlgorithm = new NoEncryption();
        _fixedKey = [];
        _limits = SerializationLimits.Default;
    }

    public static Encryptor None { get; } = new();

    public EncryptionAlgorithm DefaultKind => _defaultAlgorithm.Kind;
    public string? DefaultCustomName => _defaultAlgorithm.CustomName;
    public string? DefaultKeyId { get; }

    public byte[] Encrypt(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (plaintext.LongLength > _limits.MaxCompressedBytes)
            throw new BinaryLimitException(
                $"Plaintext length {plaintext.LongLength} exceeds the configured maximum of {_limits.MaxCompressedBytes}.");

        if (DefaultKind == EncryptionAlgorithm.None)
        {
            if (plaintext.LongLength > _limits.MaxEncryptedBytes)
                throw new BinaryLimitException(
                    $"Encrypted payload length {plaintext.LongLength} exceeds the configured maximum of {_limits.MaxEncryptedBytes}.");
            return plaintext;
        }

        bool isTransient = _keyResolver is not null;
        byte[] key = ResolveKey(DefaultKeyId);
        try
        {
            int maxLength = _defaultAlgorithm.GetMaxCiphertextLength(plaintext.Length);
            if (maxLength < 0)
                throw new InvalidOperationException(
                    "The encryption algorithm returned a negative maximum ciphertext length.");

            long configuredMaximum = _limits.MaxEncryptedBytes;
            int destinationLength = (int)Math.Min(maxLength, configuredMaximum);
            bool destinationWasCapped = destinationLength < maxLength;

            byte[] rented = ArrayPool<byte>.Shared.Rent(destinationLength);
            try
            {
                int written;
                try
                {
                    written = _defaultAlgorithm.Encrypt(plaintext, key, rented);
                }
                catch (InvalidOperationException ex) when (destinationWasCapped)
                {
                    throw new BinaryLimitException(
                        $"Encrypted payload could not fit within the configured maximum of {_limits.MaxEncryptedBytes} bytes.", ex);
                }
                catch (BinaryFormatException ex) when (destinationWasCapped)
                {
                    throw new BinaryLimitException(
                        $"Encrypted payload could not fit within the configured maximum of {_limits.MaxEncryptedBytes} bytes.", ex);
                }

                if (written < 0 || written > rented.Length)
                    throw new InvalidOperationException(
                        $"The encryption algorithm returned an invalid output length of {written}.");

                if (written > _limits.MaxEncryptedBytes)
                    throw new BinaryLimitException(
                        $"Encrypted payload length {written} exceeds the configured maximum of {_limits.MaxEncryptedBytes}.");

                return rented.AsSpan(0, written).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
        finally
        {
            if (isTransient)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Decrypt(
        EncryptionAlgorithm kind,
        string? customName,
        string? keyId,
        byte[] ciphertext,
        int expectedPlaintextLength)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.LongLength > _limits.MaxEncryptedBytes)
            throw new BinaryLimitException(
                $"Encrypted payload length {ciphertext.LongLength} exceeds the configured maximum of {_limits.MaxEncryptedBytes}.");

        if (expectedPlaintextLength < 0)
            throw new BinaryFormatException(
                $"Declared plaintext length {expectedPlaintextLength} must be non-negative.");

        if (expectedPlaintextLength > _limits.MaxCompressedBytes)
            throw new BinaryLimitException(
                $"Declared plaintext length {expectedPlaintextLength} exceeds the configured maximum of {_limits.MaxCompressedBytes}.");

        if (kind == EncryptionAlgorithm.None)
        {
            if (ciphertext.Length != expectedPlaintextLength)
                throw new BinaryFormatException(
                    $"Ciphertext length {ciphertext.Length} does not match the declared plaintext length {expectedPlaintextLength} when encryption is None.");
            return ciphertext;
        }

        var algorithm = EncryptionAlgorithmRegistry.Resolve(kind, customName);
        bool isTransient = _keyResolver is not null;
        byte[] key = ResolveKey(keyId);

        try
        {
            var result = new byte[expectedPlaintextLength];
            int written;
            try
            {
                written = algorithm.Decrypt(ciphertext, key, result);
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
                    "Decryption failed: wrong key or tampered payload.", ex);
            }

            if (written != expectedPlaintextLength)
            {
                Array.Clear(result);
                throw new BinaryFormatException(
                    $"Decryption produced {written} bytes, expected {expectedPlaintextLength}.");
            }

            return result;
        }
        finally
        {
            if (isTransient)
                CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] ResolveKey(string? keyId)
    {
        if (_keyResolver is not null)
        {
            byte[]? resolved = _keyResolver(keyId);
            if (resolved is null || resolved.Length == 0)
                throw new BinaryEncryptionKeyException(
                    $"No decryption key is available for key id '{keyId ?? "(none)"}'.");
            return resolved;
        }

        if (_fixedKey is null || _fixedKey.Length == 0)
            throw new BinaryEncryptionKeyException(
                "Encrypted payload requires key material, but no key is configured.");

        if (DefaultKeyId is not null && keyId is not null &&
            !string.Equals(DefaultKeyId, keyId, StringComparison.Ordinal))
        {
            throw new BinaryEncryptionKeyException(
                $"This data is marked as encrypted with key '{keyId}', " +
                $"but the configured encryptor is set up for key '{DefaultKeyId}'.");
        }

        return _fixedKey;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_fixedKey is not null)
            CryptographicOperations.ZeroMemory(_fixedKey);

        _disposed = true;
    }
}