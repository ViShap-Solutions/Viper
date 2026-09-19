using ViShap.Viper.Crypto;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A stand-in for an encryption algorithm that cannot authenticate associated data, used to exercise
/// the configuration gate that refuses such an algorithm under <c>RequireEncryption</c>.
/// </summary>
/// <remarks>
/// It provides no confidentiality and no integrity whatsoever — it copies bytes. It exists to make
/// <see cref="IEncryptionAlgorithm.AuthenticatesAssociatedData"/> report <see langword="false"/>, and
/// must never be used for anything else.
/// </remarks>
internal sealed class UnauthenticatedCipher : IEncryptionAlgorithm
{
    public const string RegisteredName = "test-null-cipher";

    public EncryptionAlgorithm Kind => EncryptionAlgorithm.Custom;

    public string? CustomName => RegisteredName;

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
