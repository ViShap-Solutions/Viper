namespace ViShap.Viper.Configuration;

public static class AlgorithmResolver
{
    public static ICompressor ResolveCompressor(
        ICompressionAlgorithm? algorithm = null,
        DeserializationLimits? limits = null)
    {
        if (algorithm is null ||
            algorithm.Kind == CompressionAlgorithm.None)
        {
            return Compressor.None;
        }

        return new Compressor(
            algorithm,
            limits ?? DeserializationLimits.Default);
    }

    public static ICompressor ResolveCompressor(
        CompressionAlgorithm kind,
        string? customName = null,
        DeserializationLimits? limits = null)
    {
        if (kind == CompressionAlgorithm.None)
            return Compressor.None;

        return new Compressor(
            CompressionAlgorithmRegistry.Resolve(
                kind,
                customName),
            limits ?? DeserializationLimits.Default);
    }

    public static IChecksumCalculator ResolveChecksum(
        IChecksumAlgorithm? algorithm = null) =>
        algorithm is null ||
        algorithm.Kind == ChecksumAlgorithm.None
            ? ChecksumCalculator.None
            : new ChecksumCalculator(algorithm);

    public static IChecksumCalculator ResolveChecksum(
        ChecksumAlgorithm kind,
        string? customName = null) =>
        kind == ChecksumAlgorithm.None
            ? ChecksumCalculator.None
            : new ChecksumCalculator(
                ChecksumAlgorithmRegistry.Resolve(
                    kind,
                    customName));

    public static IEncryptor ResolveEncryptor(
        IEncryptionAlgorithm? algorithm = null,
        byte[]? key = null,
        Func<string?, byte[]?>? keyResolver = null,
        string? keyId = null,
        DeserializationLimits? limits = null)
    {
        if (algorithm is null ||
            algorithm.Kind == EncryptionAlgorithm.None)
        {
            return Encryptor.None;
        }

        var actualLimits =
            limits ?? DeserializationLimits.Default;

        if (keyResolver is not null)
        {
            return new Encryptor(
                algorithm,
                keyResolver,
                keyId,
                actualLimits);
        }

        return new Encryptor(
            algorithm,
            key ?? [],
            keyId,
            actualLimits);
    }

    public static IEncryptor ResolveEncryptor(
        EncryptionAlgorithm kind,
        string? customName = null,
        byte[]? key = null,
        Func<string?, byte[]?>? keyResolver = null,
        string? keyId = null,
        DeserializationLimits? limits = null)
    {
        if (kind == EncryptionAlgorithm.None)
            return Encryptor.None;

        var algorithm =
            EncryptionAlgorithmRegistry.Resolve(
                kind,
                customName);

        var actualLimits =
            limits ?? DeserializationLimits.Default;

        if (keyResolver is not null)
        {
            return new Encryptor(
                algorithm,
                keyResolver,
                keyId,
                actualLimits);
        }

        return new Encryptor(
            algorithm,
            key ?? [],
            keyId,
            actualLimits);
    }
}