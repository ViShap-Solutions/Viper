using System.Security.Cryptography;

namespace ViShap.Viper.Crypto;

/// <summary>
/// The layout the built-in AEAD ciphers share — <c>nonce || ciphertext || tag</c> — and their common
/// checks, so each cipher states only its primitive.
/// </summary>
internal static class Aead
{
    public static void ValidateKey(ReadOnlySpan<byte> key, int keySize, string algorithm)
    {
        if (key.Length != keySize)
            throw new BinaryEncryptionKeyException(
                $"{algorithm} requires a {keySize}-byte ({keySize * 8}-bit) key, got {key.Length}.");
    }

    /// <summary>Checks that an encryption destination is exactly the ciphertext length.</summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not exactly <paramref name="length"/> bytes.</exception>
    public static void CheckDestination(Span<byte> destination, int length)
    {
        if (destination.Length != length)
            throw new ArgumentException(
                $"The destination must be exactly {length} bytes, got {destination.Length}.",
                nameof(destination));
    }

    /// <summary>The plaintext length a sealed message holds, checked against its destination.</summary>
    /// <exception cref="BinaryFormatException"><paramref name="sealedMessage"/> is shorter than a nonce and a tag.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> cannot hold the plaintext.</exception>
    public static int PlaintextLength(
        ReadOnlySpan<byte> sealedMessage,
        Span<byte> destination,
        int overhead,
        string algorithm)
    {
        if (sealedMessage.Length < overhead)
            throw new BinaryFormatException($"Ciphertext is too short for {algorithm}.");

        int plaintextLength = sealedMessage.Length - overhead;
        if (destination.Length < plaintextLength)
            throw new ArgumentException(
                $"The destination must hold at least {plaintextLength} bytes, got {destination.Length}.",
                nameof(destination));

        return plaintextLength;
    }

    public static BinaryIntegrityException AuthenticationFailed(CryptographicException cause) =>
        new("Decryption failed: wrong key, tampered payload, or tampered format metadata.", cause);
}
