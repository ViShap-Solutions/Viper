namespace ViShap.Viper.Security;

public sealed record DeserializationLimits
{
    public static DeserializationLimits Default { get; } = new();

    public int MaxDepth { get; init; } = 512;

    public int MaxArrayLength { get; init; } = 1_000_000;

    public int MaxCollectionLength { get; init; } = 1_000_000;

    public int MaxDictionaryEntries { get; init; } = 1_000_000;

    public int MaxStringLength { get; init; } = 4_000_000;

    public int MaxByteBlobLength { get; init; } = 16_000_000;

    public long MaxTotalElements { get; init; } = 10_000_000;

    public long MaxMessageBytes { get; init; } = 64L * 1024 * 1024;

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

        if (MaxStringLength <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxStringLength)} must be positive.");

        if (MaxByteBlobLength <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxByteBlobLength)} must be positive.");

        if (MaxTotalElements <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxTotalElements)} must be positive.");

        if (MaxMessageBytes <= 0)
            throw new BinaryConfigurationException($"{nameof(MaxMessageBytes)} must be positive.");
    }
}