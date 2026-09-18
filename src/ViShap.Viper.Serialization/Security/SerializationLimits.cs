namespace ViShap.Viper.Security;

public sealed record SerializationLimits
{
    public static SerializationLimits Default { get; } = new();

    public int MaxDepth { get; init; } = 512;
    public int MaxArrayLength { get; init; } = 1_000_000;
    public int MaxCollectionLength { get; init; } = 1_000_000;
    public int MaxDictionaryEntries { get; init; } = 1_000_000;
    public int MaxStringBytes { get; init; } = 4_000_000;
    public int MaxByteBlobBytes { get; init; } = 16_000_000;
    public long MaxTotalElements { get; init; } = 10_000_000;
    public long MaxObjectGraphNodes { get; init; } = 1_000_000;
    public int MaxKeyedFields { get; init; } = 1_000_000;

    public long MaxPayloadBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxCompressedBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxEncryptedBytes { get; init; } = 64L * 1024 * 1024 + 64L * 1024;
    public long MaxWireBytes { get; init; } = 80L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxDepth <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxDepth)} must be positive.");
        if (MaxArrayLength <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxArrayLength)} must be positive.");
        if (MaxCollectionLength <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxCollectionLength)} must be positive.");
        if (MaxDictionaryEntries <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxDictionaryEntries)} must be positive.");
        if (MaxStringBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxStringBytes)} must be positive.");
        if (MaxByteBlobBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxByteBlobBytes)} must be positive.");
        if (MaxTotalElements <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxTotalElements)} must be positive.");
        if (MaxObjectGraphNodes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxObjectGraphNodes)} must be positive.");
        if (MaxKeyedFields <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxKeyedFields)} must be positive.");
        if (MaxPayloadBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxPayloadBytes)} must be positive.");
        if (MaxCompressedBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxCompressedBytes)} must be positive.");
        if (MaxEncryptedBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxEncryptedBytes)} must be positive.");
        if (MaxWireBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxWireBytes)} must be positive.");
    }
}
