using ViShap.Viper.Crypto;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A key provider that keeps every key it hands out, so a test can ask what became of a resolved key
/// once the phase that requested it ended. It copies its material exactly as the built-in providers
/// do, which is what makes the recorded keys the serializer's own rather than this provider's.
/// </summary>
internal sealed class RecordingKeyProvider(byte[] material) : IKeyProvider
{
    /// <summary>The key ids <see cref="Resolve"/> was called with, in order.</summary>
    public List<string?> RequestedIds { get; } = [];

    /// <summary>Every key handed out, in order.</summary>
    public List<SecretKey> Issued { get; } = [];

    public SecretKey Resolve(string? keyId)
    {
        RequestedIds.Add(keyId);

        var key = SecretKey.CopyFrom(material);
        Issued.Add(key);
        return key;
    }
}
