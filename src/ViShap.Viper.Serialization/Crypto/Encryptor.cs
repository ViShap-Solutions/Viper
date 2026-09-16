using System.Buffers;
using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

public sealed class Encryptor : IEncryptor, IDisposable
{
    private readonly IEncryptionAlgorithm _defaultAlgorithm;
    private readonly byte[]? _fixedKey;
    private readonly Func<string?, byte[]?>? _keyResolver;
    private readonly DeserializationLimits _limits;
    private bool _disposed;

    public Encryptor(
        IEncryptionAlgorithm algorithm,
        byte[] key,
        string? keyId = null,
        DeserializationLimits? limits = null)
    {
        _defaultAlgorithm =
            algorithm ?? throw new ArgumentNullException(nameof(algorithm));

        _fixedKey =
            key ?? throw new ArgumentNullException(nameof(key));

        DefaultKeyId = keyId;
        _limits = limits ?? DeserializationLimits.Default;

        _limits.Validate();
    }

    public Encryptor(
        IEncryptionAlgorithm algorithm,
        Func<string?, byte[]?> keyResolver,
        string? keyId = null,
        DeserializationLimits? limits = null)
    {
        _defaultAlgorithm =
            algorithm ?? throw new ArgumentNullException(nameof(algorithm));

        _keyResolver =
            keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));

        DefaultKeyId = keyId;
        _limits = limits ?? DeserializationLimits.Default;

        _limits.Validate();
    }

    private Encryptor()
    {
        _defaultAlgorithm = new NoEncryption();
        _fixedKey = [];
        _limits = DeserializationLimits.Default;
    }

    public static Encryptor None { get; } = new();

    public EncryptionAlgorithm DefaultKind => _defaultAlgorithm.Kind;
    public string? DefaultCustomName => _defaultAlgorithm.CustomName;
    public string? DefaultKeyId { get; }

    public byte[] Encrypt(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (DefaultKind == EncryptionAlgorithm.None)
            return plaintext;

        bool isTransient = _keyResolver is not null;
        byte[] key = ResolveKey();

        try
        {
            int maxLength =
                _defaultAlgorithm.GetMaxCiphertextLength(
                    plaintext.Length);

            byte[] rented =
                ArrayPool<byte>.Shared.Rent(maxLength);

            try
            {
                int written =
                    _defaultAlgorithm.Encrypt(
                        plaintext,
                        key,
                        rented);

                return rented
                    .AsSpan(0, written)
                    .ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(
                    rented,
                    clearArray: true);
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
        byte[] ciphertext,
        int expectedPlaintextLength)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        
        if (kind == EncryptionAlgorithm.None) return ciphertext;

        if (expectedPlaintextLength < 0 || expectedPlaintextLength > _limits.MaxMessageBytes)
            throw new BinaryFormatException(
                $"Declared plaintext length {expectedPlaintextLength} " +
                $"exceeds the configured maximum of {_limits.MaxMessageBytes}.");

        var algorithm = EncryptionAlgorithmRegistry.Resolve(kind, customName);
        bool isTransient = _keyResolver is not null;
        byte[] key = ResolveKey();

        try
        {
            if (key.Length == 0)
            {
                throw new BinaryIntegrityException(
                    $"The payload is encrypted with '{kind}', " +
                    "but no decryption key is available.");
            }

            var result = new byte[expectedPlaintextLength];
            int written = algorithm.Decrypt(ciphertext, key, result);

            if (written != expectedPlaintextLength)
            {
                Array.Clear(result);

                throw new BinaryFormatException(
                    $"Decryption produced {written} bytes, " +
                    $"expected {expectedPlaintextLength}.");
            }

            return result;
        }
        finally
        {
            if (isTransient) CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] ResolveKey() =>
        _keyResolver is not null
            ? (_keyResolver(DefaultKeyId) ?? [])
            : _fixedKey!;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_fixedKey is not null)
            CryptographicOperations.ZeroMemory(_fixedKey);

        _disposed = true;
    }
}