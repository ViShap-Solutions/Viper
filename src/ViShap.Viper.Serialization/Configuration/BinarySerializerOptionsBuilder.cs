namespace ViShap.Viper;

public sealed class BinarySerializerOptionsBuilder
{
    internal BinarySerializerOptionsBuilder() {}

    private ICompressionAlgorithm? _compression;
    private IChecksumAlgorithm? _checksum;
    private IEncryptionAlgorithm? _encryption;
    private byte[]? _key;
    private Func<string?, byte[]?>? _keyResolver;
    private string? _keyId;
    private int _writeVersion = BinaryFormatConstants.LatestVersion;
    private bool _preserveReferences = false;
    private bool _allowV0Fallback = false;

    public BinarySerializerOptionsBuilder WithCompression(ICompressionAlgorithm compression)
    {
        _compression = compression;
        return this;
    }

    public BinarySerializerOptionsBuilder WithChecksum(IChecksumAlgorithm checksum)
    {
        _checksum = checksum;
        return this;
    }

    public BinarySerializerOptionsBuilder WithEncryption(IEncryptionAlgorithm encryption, byte[]? key = null, string? keyId = null)
    {
        _encryption = encryption;
        _key = key;
        _keyResolver = null;
        _keyId = keyId;
        return this;
    }

    public BinarySerializerOptionsBuilder WithEncryption(IEncryptionAlgorithm encryption, Func<string?, byte[]?> keyResolver, string? keyId = null)
    {
        _encryption = encryption;
        _key = null;
        _keyResolver = keyResolver;
        _keyId = keyId;
        return this;
    }

    public BinarySerializerOptionsBuilder WithVersion(int version)
    {
        _writeVersion = version;
        return this;
    }

    public BinarySerializerOptionsBuilder PreserveReferences(bool preserve = true)
    {
        _preserveReferences = preserve;
        return this;
    }
    
    public BinarySerializerOptionsBuilder AllowV0Fallback(bool allow = true)
    {
        _allowV0Fallback = allow;
        return this;
    }

    public BinarySerializerOptions Build()
    {
        return new BinarySerializerOptions
        {
            Compressor = AlgorithmResolver.ResolveCompressor(_compression),
            Checksum = AlgorithmResolver.ResolveChecksum(_checksum),
            Encryptor = AlgorithmResolver.ResolveEncryptor(_encryption, _key, _keyResolver, _keyId),
            WriteVersion = _writeVersion,
            PreserveReferences = _preserveReferences,
            AllowV0Fallback = _allowV0Fallback
        };
    }
}