using System.Collections.Concurrent;

namespace ViShap.Viper.Compression;

public static class CompressionAlgorithmRegistry
{
    private static readonly ConcurrentDictionary<CompressionAlgorithm, Func<ICompressionAlgorithm>> BuiltIn = new();
    private static readonly ConcurrentDictionary<string, Func<ICompressionAlgorithm>> Custom = new(StringComparer.Ordinal);

    static CompressionAlgorithmRegistry()
    {
        BuiltIn[CompressionAlgorithm.None] = () => new NoCompression();
        BuiltIn[CompressionAlgorithm.Deflate] = () => new Deflate();
        BuiltIn[CompressionAlgorithm.Brotli] = () => new Brotli();
    }

    public static void Register(CompressionAlgorithm kind, Func<ICompressionAlgorithm> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        BuiltIn[kind] = factory;
    }

    public static void RegisterCustom(string name, Func<ICompressionAlgorithm> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        Custom[name] = factory;
    }

    internal static ICompressionAlgorithm Resolve(CompressionAlgorithm kind, string? customName)
    {
        if (kind == CompressionAlgorithm.Custom)
        {
            if (customName is null || !Custom.TryGetValue(customName, out var customFactory))
                throw new BinaryFormatNotSupportedException($"No custom compression algorithm registered under name '{customName}'.");
            return customFactory();
        }

        if (BuiltIn.TryGetValue(kind, out var factory)) return factory();
        throw new BinaryFormatNotSupportedException($"No compression algorithm registered for '{kind}'.");
    }
}