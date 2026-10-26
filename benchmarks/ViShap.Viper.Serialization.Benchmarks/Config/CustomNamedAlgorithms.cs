using System.Buffers;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;

namespace ViShap.Viper.Serialization.Benchmarks.Config;

/// <summary>
/// The built-in algorithms presented as custom ones. Each delegates every operation to the
/// built-in implementation and differs from it in one thing: the header records <c>Custom</c> and a
/// name instead of a known kind. A differential between the two therefore isolates the header field
/// and no part of the algorithm itself (DIFF-01).
/// </summary>
internal static class CustomNamedAlgorithms
{
    internal const string CompressionName = "benchmark-deflate";
    internal const string ChecksumName = "benchmark-crc32";
    internal const string EncryptionName = "benchmark-aes-256-gcm";

    internal sealed class NamedDeflate : ICompressionAlgorithm
    {
        private readonly DeflateCompression _inner = new();

        public CompressionAlgorithm Kind => CompressionAlgorithm.Custom;

        public string? CustomName => CompressionName;

        public void Compress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination) =>
            _inner.Compress(source, destination);

        public void Decompress(ReadOnlySpan<byte> source, IBufferWriter<byte> destination, int expectedLength) =>
            _inner.Decompress(source, destination, expectedLength);
    }

    internal sealed class NamedCrc32 : IChecksumAlgorithm
    {
        private readonly Crc32Checksum _inner = new();

        public ChecksumAlgorithm Kind => ChecksumAlgorithm.Custom;

        public string? CustomName => ChecksumName;

        public int HashSizeInBytes => _inner.HashSizeInBytes;

        public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) =>
            _inner.Compute(source, destination);
    }

    internal sealed class NamedAes256Gcm : IEncryptionAlgorithm
    {
        private readonly Aes256GcmEncryption _inner = new();

        public EncryptionAlgorithm Kind => EncryptionAlgorithm.Custom;

        public string? CustomName => EncryptionName;

        public bool AuthenticatesAssociatedData => _inner.AuthenticatesAssociatedData;

        public int KeySizeInBytes => _inner.KeySizeInBytes;

        public int GetCiphertextLength(int plaintextLength) => _inner.GetCiphertextLength(plaintextLength);

        public int Encrypt(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> associatedData,
            Span<byte> destination) =>
            _inner.Encrypt(plaintext, key, associatedData, destination);

        public int Decrypt(
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> associatedData,
            Span<byte> destination) =>
            _inner.Decrypt(ciphertext, key, associatedData, destination);
    }
}
