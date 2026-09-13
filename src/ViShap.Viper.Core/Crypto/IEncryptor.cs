namespace ViShap.Viper.Crypto;

public interface IEncryptor
{
    EncryptionAlgorithm DefaultKind { get; }
    string? DefaultCustomName { get; }
    string? DefaultKeyId { get; }

    byte[] Encrypt(byte[] plaintext);
    byte[] Decrypt(EncryptionAlgorithm kind, string? customName, byte[] ciphertext, int expectedPlaintextLength);
}