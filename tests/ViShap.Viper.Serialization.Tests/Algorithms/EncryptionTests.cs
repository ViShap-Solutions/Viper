using System.Security.Cryptography;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins ENC-01, ENC-03…ENC-05, ENC-07, ENC-09…ENC-14, ENC-17, ENC-20 and CFG-05: authenticated
/// metadata, key ownership, and the difference between being able to decrypt and requiring it.
/// </summary>
public class EncryptionTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static BinarySerializer Encrypted(byte[] key, string? keyId = null) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), key, keyId)
            .Build());

    // --- capability is not policy ---------------------------------------------------------------

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

    // --- authenticated metadata -----------------------------------------------------------------

    [Fact]
    public void Deserialize_EncryptedPayload_RoundTrips()
    {
        var serializer = Encrypted(NewKey());
        var source = new Person { Name = "Alice", Age = 30 };

        var result = serializer.Deserialize<Person>(serializer.Serialize(source));

        Assert.Equivalent(source, result);
    }

    [Fact]
    public void Deserialize_EncryptedPayloadWithTamperedHeaderFlag_ThrowsIntegrity()
    {
        var serializer = Encrypted(NewKey());
        byte[] payload = serializer.Serialize(123);

        byte[] tampered = Mutate.SetByte(payload, Wire.PreserveReferencesOffset, 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<int>(tampered));
    }

    [Fact]
    public void Deserialize_EncryptedPayloadWithAnyHeaderByteFlipped_AlwaysFails()
    {
        // The exception family is genuine here: flipping a version or an algorithm identifier is
        // BinaryFormatNotSupportedException, a length is BinaryFormatException, and an authenticated
        // field is BinaryIntegrityException. Contract §13.1 promises only that no flip is accepted.
        var serializer = Encrypted(NewKey());
        byte[] original = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        for (int index = 8; index < Wire.PlainHeaderLength; index++)
        {
            byte[] tampered = Mutate.FlipByte(original, index);

            Assert.ThrowsAny<BinarySerializerException>(
                () => serializer.Deserialize<Person>(tampered));
        }
    }

    [Fact]
    public void Deserialize_EncryptedPayloadWithTamperedCiphertext_ThrowsIntegrity()
    {
        var serializer = Encrypted(NewKey());
        byte[] payload = serializer.Serialize(new Person { Name = "Alice", Age = 30 });

        byte[] tampered = Mutate.FlipByte(payload, payload.Length - 1);

        Assert.Throws<BinaryIntegrityException>(() => serializer.Deserialize<Person>(tampered));
    }

    [Fact]
    public void Deserialize_WithTheWrongKey_ThrowsIntegrity()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);

        Assert.Throws<BinaryIntegrityException>(() => Encrypted(NewKey()).Deserialize<int>(payload));
    }

    [Fact]
    public void Deserialize_EncryptedPayloadWithoutAnyKey_ThrowsKeyException()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);

        Assert.Throws<BinaryEncryptionKeyException>(
            () => new BinarySerializer().Deserialize<int>(payload));
    }

    // --- key ownership --------------------------------------------------------------------------

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

    [Fact]
    public void Deserialize_WithAKeyResolver_ReceivesTheHeaderKeyId()
    {
        byte[] key = NewKey();
        byte[] payload = Encrypted(key, "primary").Serialize(123);

        string? observed = null;
        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), id => { observed = id; return key; }, "primary")
                .Build());

        Assert.Equal(123, reader.Deserialize<int>(payload));
        Assert.Equal("primary", observed);
    }
}
