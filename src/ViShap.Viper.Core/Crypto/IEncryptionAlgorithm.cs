namespace ViShap.Viper.Crypto;

public interface IEncryptionAlgorithm
{
    EncryptionAlgorithm Kind { get; }
    string? CustomName { get; }
    int GetMaxCiphertextLength(int plaintextLength);
    int Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination);
    int Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, Span<byte> destination);
}