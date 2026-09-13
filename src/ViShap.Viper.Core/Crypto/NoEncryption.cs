namespace ViShap.Viper.Crypto;

public sealed class NoEncryption : IEncryptionAlgorithm
{
    public EncryptionAlgorithm Kind => EncryptionAlgorithm.None;
    public string? CustomName => null;
    public int GetMaxCiphertextLength(int plaintextLength) => plaintextLength;

    public int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination)
    {
        plaintext.CopyTo(destination);
        return plaintext.Length;
    }

    public int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, Span<byte> destination)
    {
        ciphertext.CopyTo(destination);
        return ciphertext.Length;
    }
}