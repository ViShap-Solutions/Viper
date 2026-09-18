namespace ViShap.Viper.Crypto;

/// <summary>
/// The pass-through encryption algorithm, used when encryption is disabled. Payload bytes are stored
/// exactly as produced, and no key is required.
/// </summary>
public sealed class NoEncryption : IEncryptionAlgorithm
{
    /// <inheritdoc />
    public EncryptionAlgorithm Kind => EncryptionAlgorithm.None;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int GetMaxCiphertextLength(int plaintextLength) => plaintextLength;

    /// <inheritdoc />
    public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination)
    {
        plaintext.CopyTo(destination);
        return plaintext.Length;
    }

    /// <inheritdoc />
    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, Span<byte> destination)
    {
        ciphertext.CopyTo(destination);
        return ciphertext.Length;
    }
}
