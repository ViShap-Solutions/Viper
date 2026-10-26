using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins ENC-27 and ENC-31: <see cref="HkdfKeyProvider"/> derives one key per key id from a root key it never
/// exposes, each an owned copy, and refuses a payload that names no id.
/// </summary>
public class HkdfKeyProviderTests
{
    private static readonly byte[] RootKey = RandomNumberGenerator.GetBytes(32);

    // --- ENC-27: one key per id, the root key never exposed ------------------------------------------

    [Fact]
    public void Resolve_TheSameId_DerivesTheSameKey()
    {
        using var provider = new HkdfKeyProvider(RootKey);

        using var first = provider.Resolve("tenant-1");
        using var second = provider.Resolve("tenant-1");

        Assert.Equal(first.Span.ToArray(), second.Span.ToArray());
    }

    [Fact]
    public void Resolve_DifferentIds_DeriveDifferentKeys()
    {
        using var provider = new HkdfKeyProvider(RootKey);

        using var first = provider.Resolve("tenant-1");
        using var second = provider.Resolve("tenant-2");

        Assert.NotEqual(first.Span.ToArray(), second.Span.ToArray());
    }

    [Fact]
    public void Resolve_AnId_IsHkdfSha256OfTheRootKeyWithTheIdAsInfo()
    {
        byte[] salt = [1, 2, 3];
        using var provider = new HkdfKeyProvider(RootKey, salt: salt);

        using var key = provider.Resolve("tenant-1");

        byte[] expected = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, RootKey, 32, salt, Encoding.UTF8.GetBytes("tenant-1"));
        Assert.Equal(expected, key.Span.ToArray());
    }

    [Fact]
    public void Resolve_AnId_NeverYieldsTheRootKey()
    {
        using var provider = new HkdfKeyProvider(RootKey);

        using var key = provider.Resolve("tenant-1");

        Assert.NotEqual(RootKey, key.Span.ToArray());
    }

    [Fact]
    public void HkdfKeyProvider_ExposesNoMemberBeyondResolveAndDispose()
    {
        string[] members =
        [
            .. typeof(HkdfKeyProvider)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member => member.MemberType != MemberTypes.Constructor)
                .Select(member => member.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.Equal(["Dispose", "Resolve"], members);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void Resolve_AConfiguredKeySize_DerivesKeysOfThatLength(int keySize)
    {
        using var provider = new HkdfKeyProvider(RootKey, keySize);

        using var key = provider.Resolve("tenant-1");

        Assert.Equal(keySize, key.Length);
    }

    [Fact]
    public void Resolve_ADerivedKey_IsAnOwnedCopy()
    {
        using var provider = new HkdfKeyProvider(RootKey);

        var first = provider.Resolve("tenant-1");
        byte[] material = first.Span.ToArray();
        first.Dispose();

        using var second = provider.Resolve("tenant-1");
        Assert.Equal(material, second.Span.ToArray());
    }

    [Fact]
    public void Constructor_CopiesTheRootKey_SoClearingTheCallersArrayChangesNothing()
    {
        byte[] root = RandomNumberGenerator.GetBytes(32);
        using var provider = new HkdfKeyProvider(root);
        using var before = provider.Resolve("tenant-1");

        root.AsSpan().Clear();

        using var after = provider.Resolve("tenant-1");
        Assert.Equal(before.Span.ToArray(), after.Span.ToArray());
    }

    [Fact]
    public void Dispose_LeavesTheCallersArrayUntouched()
    {
        byte[] root = RandomNumberGenerator.GetBytes(32);
        byte[] original = [.. root];

        new HkdfKeyProvider(root).Dispose();

        Assert.Equal(original, root);
    }

    [Fact]
    public void Resolve_NoKeyId_ThrowsKeyException()
    {
        using var provider = new HkdfKeyProvider(RootKey);

        AssertEx.Throws<BinaryEncryptionKeyException>("key id", () => provider.Resolve(null));
    }

    [Fact]
    public void Resolve_AfterDispose_ThrowsObjectDisposed()
    {
        var provider = new HkdfKeyProvider(RootKey);
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.Resolve("tenant-1"));
    }

    [Fact]
    public void Constructor_AnEmptyRootKey_ThrowsKeyException()
    {
        Assert.Throws<BinaryEncryptionKeyException>(() => new HkdfKeyProvider([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(255 * 32 + 1)]
    public void Constructor_AKeySizeHkdfCannotDerive_ThrowsArgumentOutOfRange(int keySize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HkdfKeyProvider(RootKey, keySize));
    }

    [Fact]
    public void Deserialize_PayloadsWrittenUnderTwoIds_AreReadByOneProvider()
    {
        using var keys = new HkdfKeyProvider(RootKey);
        byte[] first = Writer(keys, "tenant-1").Serialize(1);
        byte[] second = Writer(keys, "tenant-2").Serialize(2);

        using var readerKeys = new HkdfKeyProvider(RootKey);
        var reader = new BinarySerializer(BinarySerializerOptions.Configure().WithKeys(readerKeys).Build());

        Assert.Equal(1, reader.Deserialize<int>(first));
        Assert.Equal(2, reader.Deserialize<int>(second));
    }

    [Fact]
    public void Deserialize_APayloadWrittenUnderAnotherRootKey_ThrowsIntegrity()
    {
        using var keys = new HkdfKeyProvider(RootKey);
        byte[] frame = Writer(keys, "tenant-1").Serialize(1);

        using var otherKeys = new HkdfKeyProvider(RandomNumberGenerator.GetBytes(32));
        var reader = new BinarySerializer(BinarySerializerOptions.Configure().WithKeys(otherKeys).Build());

        Assert.Throws<BinaryIntegrityException>(() => reader.Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_WithoutAKeyId_ThrowsKeyException()
    {
        using var keys = new HkdfKeyProvider(RootKey);

        Assert.Throws<BinaryEncryptionKeyException>(() => Writer(keys, keyId: null).Serialize(1));
    }

    private static BinarySerializer Writer(HkdfKeyProvider keys, string? keyId) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256GcmEncryption(), keys, keyId)
            .Build());

    // --- ENC-31: a key id UTF-8 cannot encode derives nothing ----------------------------------------

    [Fact]
    public void Resolve_AnIdWithALoneSurrogate_ThrowsKeyInsteadOfSharingAKey()
    {
        using var provider = new HkdfKeyProvider(RootKey);

        AssertEx.Throws<BinaryEncryptionKeyException>("surrogate", () => provider.Resolve("k" + (char)0xD800));
        AssertEx.Throws<BinaryEncryptionKeyException>("surrogate", () => provider.Resolve("k" + (char)0xDC00));
    }

    [Fact]
    public void Serialize_AKeyIdWithALoneSurrogate_ThrowsConfigurationAndWritesNothing()
    {
        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256GcmEncryption(), RootKey, "k" + (char)0xD800)
            .Build());
        var destination = new System.Buffers.ArrayBufferWriter<byte>();

        AssertEx.Throws<BinaryConfigurationException>("surrogate", () => serializer.Serialize(destination, 1));
        Assert.Equal(0, destination.WrittenCount);
    }
}
