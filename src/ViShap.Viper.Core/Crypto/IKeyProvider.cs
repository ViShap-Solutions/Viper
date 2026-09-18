namespace ViShap.Viper.Crypto;

/// <summary>
/// Supplies key material for a given key id. The returned <see cref="SecretKey"/> is owned by the
/// caller of <see cref="Resolve"/>, which disposes it; a provider therefore always hands out a copy
/// and never exposes its own buffer.
/// </summary>
public interface IKeyProvider
{
    /// <summary>
    /// Resolves the key selected by <paramref name="keyId"/> (the id recorded in the payload header,
    /// or <c>null</c> when the payload does not name one).
    /// </summary>
    /// <exception cref="BinaryEncryptionKeyException">No usable key is available for the id.</exception>
    SecretKey Resolve(string? keyId);
}
