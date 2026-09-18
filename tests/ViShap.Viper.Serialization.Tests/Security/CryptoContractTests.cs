using System.Security.Cryptography;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Security;

/// <summary>
/// Acceptance gate for the cryptography findings (S01, S02, S07, S08, S09): key ownership,
/// authenticated metadata, downgrade policy and exact decompression.
/// </summary>
public class CryptoContractTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static BinarySerializer Encrypted(byte[] key, string? keyId = null) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), key, keyId)
            .Build());

    // --- S01: encryption capability is not encryption policy -----------------------------------

    [Fact]
    public void Deserialize_PlaintextPayload_IsAcceptedWhenEncryptionIsOnlyACapability()
    {
        byte[] plaintext = new BinarySerializer().Serialize(123);

        Assert.Equal(123, Encrypted(NewKey()).Deserialize<int>(plaintext));
    }

    [Fact]
    public void Deserialize_PlaintextPayload_IsRejectedWhenEncryptionIsRequired()
    {
        byte[] plaintext = new BinarySerializer().Serialize(123);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), NewKey())
                .RequireEncryption()
                .Build());

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<int>(plaintext));
    }

    [Fact]
    public void Build_RequireEncryptionWithoutAlgorithm_ThrowsConfiguration()
    {
        Assert.Throws<BinaryConfigurationException>(
            () => BinarySerializerOptions.Configure().RequireEncryption().Build());
    }

    // --- S02: the header is authenticated ------------------------------------------------------

    [Fact]
    public void Deserialize_EncryptedPayloadWithTamperedHeaderFlag_ThrowsIntegrity()
    {
        var serializer = Encrypted(NewKey());
        byte[] payload = serializer.Serialize(123);

        payload[15] = 1;   // PreserveReferences, outside the ciphertext

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<int>(payload));
    }

    [Fact]
    public void Deserialize_EncryptedPayloadWithEveryHeaderByteFlipped_AlwaysFails()
    {
        var serializer = Encrypted(NewKey());
        byte[] original = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        // The header runs up to the on-disk payload; flipping any of it must be detected.
        const int headerLength = 30;
        for (int index = 8; index < headerLength; index++)
        {
            byte[] tampered = (byte[])original.Clone();
            tampered[index] ^= 0xFF;

            Assert.ThrowsAny<BinarySerializerException>(
                () => serializer.Deserialize<Person>(tampered));
        }
    }

    [Fact]
    public void Deserialize_EncryptedPayload_RoundTrips()
    {
        var serializer = Encrypted(NewKey());
        var source = new Person { Name = "Alice", Age = 30 };

        var result = serializer.Deserialize<Person>(serializer.Serialize(source));

        Assert.Equivalent(source, result);
    }

    // --- S07: a key provider's buffer is never mutated ------------------------------------------

    [Fact]
    public void Encrypt_WithKeyResolver_LeavesTheCallersBufferIntact()
    {
        byte[] shared = NewKey();
        byte[] expected = (byte[])shared.Clone();

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), _ => shared)
                .Build());

        byte[] first = serializer.Serialize(1);
        byte[] second = serializer.Serialize(2);

        Assert.Equal(expected, shared);
        Assert.Equal(1, serializer.Deserialize<int>(first));
        Assert.Equal(2, serializer.Deserialize<int>(second));
    }

    [Fact]
    public void Decrypt_SecondPayload_DoesNotFallBackToAZeroKey()
    {
        byte[] shared = NewKey();

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), _ => shared)
                .Build());

        serializer.Serialize(1);
        byte[] second = serializer.Serialize(2);

        var zeroKeyed = Encrypted(new byte[32]);
        Assert.Throws<BinaryIntegrityException>(() => zeroKeyed.Deserialize<int>(second));
    }

    // --- S08: disposal only clears what the serializer owns -------------------------------------

    [Fact]
    public void Dispose_KeyProvider_LeavesTheCallersKeyIntact()
    {
        byte[] caller = NewKey();
        byte[] expected = (byte[])caller.Clone();

        var provider = new StaticKeyProvider(caller);
        provider.Dispose();

        Assert.Equal(expected, caller);
    }

    [Fact]
    public void Resolve_AfterDispose_ThrowsObjectDisposed()
    {
        var provider = new StaticKeyProvider(NewKey());
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.Resolve(null));
    }

    [Fact]
    public void Resolve_ReturnsAnOwnedCopyThatCallersMayDispose()
    {
        using var provider = new StaticKeyProvider(NewKey());

        using (var first = provider.Resolve(null))
            Assert.Equal(32, first.Length);

        using var second = provider.Resolve(null);
        Assert.Equal(32, second.Length);
    }

    [Fact]
    public void Resolve_WithMismatchedKeyId_ThrowsKeyException()
    {
        using var provider = new StaticKeyProvider(NewKey(), "primary");

        Assert.Throws<BinaryEncryptionKeyException>(() => provider.Resolve("rotated"));
    }

    // --- S09: decompression produces exactly the declared size ----------------------------------

    [Fact]
    public void Decompress_OutputLongerThanDeclared_ThrowsFormat()
    {
        var deflate = new Deflate();
        byte[] source = new byte[1024];

        byte[] compressed = new byte[deflate.GetMaxCompressedLength(source.Length)];
        int length = deflate.Compress(source, compressed);

        Assert.Throws<BinaryFormatException>(
            () => deflate.Decompress(compressed.AsSpan(0, length), new byte[4]));
    }

    [Fact]
    public void Deserialize_CompressedPayload_RoundTrips()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithCompression(new Deflate()).Build());

        var source = new Person { Name = new string('x', 5_000), Age = 7 };
        var result = serializer.Deserialize<Person>(serializer.Serialize(source));

        Assert.Equivalent(source, result);
    }
}
