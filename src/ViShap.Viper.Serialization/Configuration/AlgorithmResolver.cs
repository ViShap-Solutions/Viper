namespace ViShap.Viper.Configuration;

public static class AlgorithmResolver
{
    public static ICompressor ResolveCompressor(
        ICompressionAlgorithm? algorithm = null,
        SerializationLimits? limits = null)
    {
        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        if (algorithm is null ||
            algorithm.Kind == CompressionAlgorithm.None)
        {
            return new Compressor(new NoCompression(), actualLimits);
        }

        return new Compressor(
            algorithm,
            actualLimits);
    }

    public static ICompressor ResolveCompressor(
        CompressionAlgorithm kind,
        string? customName = null,
        SerializationLimits? limits = null)
    {
        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        if (kind == CompressionAlgorithm.None)
            return new Compressor(new NoCompression(), actualLimits);

        return new Compressor(
            CompressionAlgorithmRegistry.Resolve(
                kind,
                customName),
                actualLimits);
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
        SerializationLimits? limits = null)
    {
        var actualLimits =
            limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        if (algorithm is null ||
            algorithm.Kind == EncryptionAlgorithm.None)
        {
            return Encryptor.None;
        }

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
        SerializationLimits? limits = null)
    {
        var actualLimits =
            limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        if (kind == EncryptionAlgorithm.None)
            return Encryptor.None;

        var algorithm =
            EncryptionAlgorithmRegistry.Resolve(
                kind,
                customName);

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