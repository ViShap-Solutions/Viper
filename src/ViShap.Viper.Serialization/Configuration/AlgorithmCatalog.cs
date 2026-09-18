using System.Collections.Frozen;

namespace ViShap.Viper.Configuration;

/// <summary>
/// The set of algorithms one serializer may use, resolved from a payload header. Built-in algorithms
/// are fixed — nothing can substitute the implementation of <c>Aes256Gcm</c> for another serializer
/// or for the process — and custom algorithms come from the options that were built, not from global
/// mutable state.
/// </summary>
internal sealed class AlgorithmCatalog
{
    public static AlgorithmCatalog BuiltIn { get; } = new(
        FrozenDictionary<string, Func<ICompressionAlgorithm>>.Empty,
        FrozenDictionary<string, Func<IChecksumAlgorithm>>.Empty,
        FrozenDictionary<string, Func<IEncryptionAlgorithm>>.Empty);

    private readonly FrozenDictionary<string, Func<ICompressionAlgorithm>> _compression;
    private readonly FrozenDictionary<string, Func<IChecksumAlgorithm>> _checksum;
    private readonly FrozenDictionary<string, Func<IEncryptionAlgorithm>> _encryption;

    private AlgorithmCatalog(
        FrozenDictionary<string, Func<ICompressionAlgorithm>> compression,
        FrozenDictionary<string, Func<IChecksumAlgorithm>> checksum,
        FrozenDictionary<string, Func<IEncryptionAlgorithm>> encryption)
    {
        _compression = compression;
        _checksum = checksum;
        _encryption = encryption;
    }

    public static AlgorithmCatalog Create(
        IReadOnlyDictionary<string, Func<ICompressionAlgorithm>> compression,
        IReadOnlyDictionary<string, Func<IChecksumAlgorithm>> checksum,
        IReadOnlyDictionary<string, Func<IEncryptionAlgorithm>> encryption) =>
        compression.Count == 0 && checksum.Count == 0 && encryption.Count == 0
            ? BuiltIn
            : new AlgorithmCatalog(
                compression.ToFrozenDictionary(StringComparer.Ordinal),
                checksum.ToFrozenDictionary(StringComparer.Ordinal),
                encryption.ToFrozenDictionary(StringComparer.Ordinal));

    public ICompressionAlgorithm ResolveCompression(CompressionAlgorithm kind, string? customName) =>
        kind switch
        {
            CompressionAlgorithm.None => new NoCompression(),
            CompressionAlgorithm.Deflate => new Deflate(),
            CompressionAlgorithm.Brotli => new Brotli(),
            CompressionAlgorithm.Custom => ResolveCustom(_compression, customName, "compression"),
            _ => throw new BinaryFormatNotSupportedException(
                $"No compression algorithm registered for '{kind}'.")
        };

    public IChecksumAlgorithm ResolveChecksum(ChecksumAlgorithm kind, string? customName) =>
        kind switch
        {
            ChecksumAlgorithm.None => new NoChecksum(),
            ChecksumAlgorithm.Crc32 => new Crc32(),
            ChecksumAlgorithm.Custom => ResolveCustom(_checksum, customName, "checksum"),
            _ => throw new BinaryFormatNotSupportedException(
                $"No checksum algorithm registered for '{kind}'.")
        };

    public IEncryptionAlgorithm ResolveEncryption(EncryptionAlgorithm kind, string? customName) =>
        kind switch
        {
            EncryptionAlgorithm.None => new NoEncryption(),
            EncryptionAlgorithm.Aes256Gcm => new Aes256Gcm(),
            EncryptionAlgorithm.Custom => ResolveCustom(_encryption, customName, "encryption"),
            _ => throw new BinaryFormatNotSupportedException(
                $"No encryption algorithm registered for '{kind}'.")
        };

    private static T ResolveCustom<T>(
        FrozenDictionary<string, Func<T>> registrations,
        string? customName,
        string what)
    {
        if (customName is null || !registrations.TryGetValue(customName, out var factory))
            throw new BinaryFormatNotSupportedException(
                $"No custom {what} algorithm is registered under the name " +
                $"'{customName ?? "(none)"}'. Register it on the options builder before reading a " +
                "payload that names it.");

        return factory()
               ?? throw new BinaryConfigurationException(
                   $"The custom {what} algorithm factory for '{customName}' returned null.");
    }
}
