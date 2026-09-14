namespace ViShap.Viper;

public sealed record BinarySerializerOptions
{
    internal BinarySerializerOptions() { }
    public static BinarySerializerOptions Default { get; } = new();
    public static BinarySerializerOptionsBuilder Configure() => new();

    public ICompressor Compressor { get; init; } = Compression.Compressor.None;
    public IChecksumCalculator Checksum { get; init; } = ChecksumCalculator.None;
    public IEncryptor Encryptor { get; init; } = Crypto.Encryptor.None;
    public int WriteVersion { get; init; } = BinaryFormatConstants.LatestVersion;
    public bool PreserveReferences { get; init; }
    public DeserializationLimits Limits { get; init; } = DeserializationLimits.Default;
    public bool AllowV0Fallback { get; init; }

    public static BinarySerializerOptions FromHeader(
        BinaryHeaderInfo info,
        byte[]? key = null,
        DeserializationLimits? limits = null)
    {
        var actualLimits = limits ?? DeserializationLimits.Default;

        return new BinarySerializerOptions
        {
            Compressor =
                AlgorithmResolver.ResolveCompressor(
                    info.Compression,
                    info.CustomCompressionName,
                    actualLimits),

            Checksum =
                AlgorithmResolver.ResolveChecksum(
                    info.ChecksumAlgorithm,
                    info.CustomChecksumName),

            Encryptor =
                AlgorithmResolver.ResolveEncryptor(
                    info.Encryption,
                    info.CustomEncryptionName,
                    key: key,
                    keyId: info.KeyId,
                    limits: actualLimits),

            Limits = actualLimits
        };
    }

    public static BinarySerializerOptions FromHeader(
        BinaryHeaderInfo info,
        Func<string?, byte[]?> keyResolver,
        DeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(keyResolver);

        var actualLimits =
            limits ?? DeserializationLimits.Default;

        return new BinarySerializerOptions
        {
            Compressor =
                AlgorithmResolver.ResolveCompressor(
                    info.Compression,
                    info.CustomCompressionName,
                    actualLimits),

            Checksum =
                AlgorithmResolver.ResolveChecksum(
                    info.ChecksumAlgorithm,
                    info.CustomChecksumName),

            Encryptor =
                AlgorithmResolver.ResolveEncryptor(
                    info.Encryption,
                    info.CustomEncryptionName,
                    keyResolver: keyResolver,
                    keyId: info.KeyId,
                    limits: actualLimits),

            Limits = actualLimits
        };
    }

    public static BinarySerializerOptions FromStream(
        Stream stream,
        byte[]? key = null,
        DeserializationLimits? limits = null)
    {
        var info =
            BinaryFormatInspector.Peek(stream)
            ?? throw new BinaryFormatException(
                "Unable to inspect stream header. Format is unknown or unsupported.");

        return FromHeader(info, key, limits);
    }

    public static BinarySerializerOptions FromStream(
        Stream stream,
        Func<string?, byte[]?> keyResolver,
        DeserializationLimits? limits = null)
    {
        var info =
            BinaryFormatInspector.Peek(stream)
            ?? throw new BinaryFormatException(
                "Unable to inspect stream header. Format is unknown or unsupported.");

        return FromHeader(info, keyResolver, limits);
    }
}