using System.Collections.Concurrent;

namespace ViShap.Viper.Crypto;

public static class EncryptionAlgorithmRegistry
{
    private static readonly ConcurrentDictionary<EncryptionAlgorithm, Func<IEncryptionAlgorithm>> BuiltIn = new();
    private static readonly ConcurrentDictionary<string, Func<IEncryptionAlgorithm>> Custom = new(StringComparer.Ordinal);

    static EncryptionAlgorithmRegistry()
    {
        BuiltIn[EncryptionAlgorithm.None] = () => new NoEncryption();
        BuiltIn[EncryptionAlgorithm.Aes256Gcm] = () => new Aes256Gcm();
    }

    public static void Register(EncryptionAlgorithm kind, Func<IEncryptionAlgorithm> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        BuiltIn[kind] = factory;
    }

    public static void RegisterCustom(string name, Func<IEncryptionAlgorithm> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        Custom[name] = factory;
    }

    internal static IEncryptionAlgorithm Resolve(EncryptionAlgorithm kind, string? customName)
    {
        if (kind == EncryptionAlgorithm.Custom)
        {
            if (customName is null || !Custom.TryGetValue(customName, out var customFactory))
                throw new BinaryFormatNotSupportedException($"No custom encryption algorithm registered under name '{customName}'.");
            return customFactory();
        }

        if (BuiltIn.TryGetValue(kind, out var factory)) return factory();
        throw new BinaryFormatNotSupportedException($"No encryption algorithm registered for '{kind}'.");
    }
}