using System.Collections.Concurrent;

namespace ViShap.Viper.Checksum;

public static class ChecksumAlgorithmRegistry
{
    private static readonly ConcurrentDictionary<ChecksumAlgorithm, Func<IChecksumAlgorithm>> BuiltIn = new();
    private static readonly ConcurrentDictionary<string, Func<IChecksumAlgorithm>> Custom = new(StringComparer.Ordinal);

    static ChecksumAlgorithmRegistry()
    {
        BuiltIn[ChecksumAlgorithm.None] = () => new NoChecksum();
        BuiltIn[ChecksumAlgorithm.Crc32] = () => new Crc32();
    }

    public static void Register(ChecksumAlgorithm kind, Func<IChecksumAlgorithm> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        BuiltIn[kind] = factory;
    }

    public static void RegisterCustom(string name, Func<IChecksumAlgorithm> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        Custom[name] = factory;
    }

    internal static IChecksumAlgorithm Resolve(ChecksumAlgorithm kind, string? customName)
    {
        if (kind == ChecksumAlgorithm.Custom)
        {
            if (customName is null || !Custom.TryGetValue(customName, out var customFactory))
                throw new BinaryFormatNotSupportedException($"No custom checksum algorithm registered under name '{customName}'.");
            return customFactory();
        }

        if (BuiltIn.TryGetValue(kind, out var factory)) return factory();
        throw new BinaryFormatNotSupportedException($"No checksum algorithm registered for '{kind}'.");
    }
}