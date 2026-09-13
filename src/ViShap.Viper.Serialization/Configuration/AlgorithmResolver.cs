namespace ViShap.Viper.Configuration;

public static class AlgorithmResolver
{
    public static ICompressor ResolveCompressor(ICompressionAlgorithm? algorithm = null) =>
        algorithm is null || algorithm.Kind == CompressionAlgorithm.None
            ? Compressor.None
            : new Compressor(algorithm);

    public static ICompressor ResolveCompressor(CompressionAlgorithm kind, string? customName = null) =>
        kind == CompressionAlgorithm.None
            ? Compressor.None
            : new Compressor(CompressionAlgorithmRegistry.Resolve(kind, customName));

    public static IChecksumCalculator ResolveChecksum(IChecksumAlgorithm? algorithm = null) =>
        algorithm is null || algorithm.Kind == ChecksumAlgorithm.None
            ? ChecksumCalculator.None
            : new ChecksumCalculator(algorithm);

    public static IChecksumCalculator ResolveChecksum(ChecksumAlgorithm kind, string? customName = null) =>
        kind == ChecksumAlgorithm.None
            ? ChecksumCalculator.None
            : new ChecksumCalculator(ChecksumAlgorithmRegistry.Resolve(kind, customName));

    public static IEncryptor ResolveEncryptor(
        IEncryptionAlgorithm? algorithm = null, 
        byte[]? key = null, 
        Func<string?, byte[]?>? keyResolver = null, 
        string? keyId = null)
    {
        if (algorithm is null || algorithm.Kind == EncryptionAlgorithm.None)
            return Encryptor.None;

        if (keyResolver is not null)
            return new Encryptor(algorithm, keyResolver, keyId);

        return new Encryptor(algorithm, key ?? [], keyId);
    }

    public static IEncryptor ResolveEncryptor(
        EncryptionAlgorithm kind, 
        string? customName = null, 
        byte[]? key = null, 
        Func<string?, byte[]?>? keyResolver = null, 
        string? keyId = null)
    {
        if (kind == EncryptionAlgorithm.None) 
            return Encryptor.None;

        var algorithm = EncryptionAlgorithmRegistry.Resolve(kind, customName);

        if (keyResolver is not null)
            return new Encryptor(algorithm, keyResolver, keyId);

        return new Encryptor(algorithm, key ?? [], keyId);
    }
}