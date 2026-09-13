using System.Buffers;
using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

public sealed class Encryptor : IEncryptor, IDisposable
{
    private readonly IEncryptionAlgorithm _defaultAlgorithm ;
    private readonly byte[]? _fixedKey;
    private readonly Func<string?, byte[]?>? _keyResolver;
    private bool _disposed;

    public Encryptor(IEncryptionAlgorithm algorithm, byte[] key, string? keyId = null)
    {
        _defaultAlgorithm = algorithm;
        _fixedKey = key ?? throw new ArgumentNullException(nameof(key));
        DefaultKeyId = keyId;
    }

    public Encryptor(IEncryptionAlgorithm algorithm, Func<string?, byte[]?> keyResolver, string? keyId = null)
    {
        _defaultAlgorithm = algorithm;
        _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
        DefaultKeyId = keyId;
    }

    private Encryptor()
    {
        _defaultAlgorithm = new NoEncryption();
        _fixedKey = [];
    }

    public static Encryptor None { get; } = new();

    public EncryptionAlgorithm DefaultKind => _defaultAlgorithm.Kind;
    public string? DefaultCustomName => _defaultAlgorithm.CustomName;
    public string? DefaultKeyId { get; }

    public byte[] Encrypt(byte[] plaintext)
    {
        if (DefaultKind == EncryptionAlgorithm.None) return plaintext;

        bool isTransient = _keyResolver is not null;
        byte[] key = ResolveKey();

        try
        {
            int maxLength = _defaultAlgorithm.GetMaxCiphertextLength(plaintext.Length);
            byte[] rented = ArrayPool<byte>.Shared.Rent(maxLength);
            try
            {
                int written = _defaultAlgorithm.Encrypt(plaintext, key, rented);
                return rented.AsSpan(0, written).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
        finally
        {
            if (isTransient) CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Decrypt(EncryptionAlgorithm kind, string? customName, byte[] ciphertext, int expectedPlaintextLength)
    {
        if (kind == EncryptionAlgorithm.None) return ciphertext;

        var algorithm = EncryptionAlgorithmRegistry.Resolve(kind, customName);
        bool isTransient = _keyResolver is not null;
        byte[] key = ResolveKey();

        try
        {
            var result = new byte[expectedPlaintextLength];
            int written = algorithm.Decrypt(ciphertext, key, result);

            if (written != expectedPlaintextLength)
            {
                Array.Clear(result);
                throw new BinaryFormatException($"Decryption produced {written} bytes, expected {expectedPlaintextLength}.");
            }

            return result;
        }
        finally
        {
            if (isTransient) CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] ResolveKey() => _keyResolver is not null 
        ? (_keyResolver(DefaultKeyId) ?? []) 
        : _fixedKey!;

    public void Dispose()
    {
        if (_disposed) return;
        if (_fixedKey is not null) CryptographicOperations.ZeroMemory(_fixedKey);
        _disposed = true;
    }
}