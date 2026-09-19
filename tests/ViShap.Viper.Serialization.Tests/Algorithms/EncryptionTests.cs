using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins ENC-01…ENC-05, ENC-07, ENC-09…ENC-17 and ENC-19…ENC-22, and CFG-05: authenticated metadata,
/// key ownership, the phase ceilings encryption answers to, and the difference between being able to
/// decrypt and requiring it.
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

    // --- ENC-02: a key the algorithm cannot use -------------------------------------------------

    [Fact]
    public void Serialize_WithAKeyOfTheWrongSizeForTheAlgorithm_ThrowsKeyException()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), new byte[16])
                .Build());

        AssertEx.Throws<BinaryEncryptionKeyException>("32-byte", () => serializer.Serialize(123));
    }

    [Fact]
    public void Deserialize_WithAKeyOfTheWrongSizeForTheAlgorithm_ThrowsKeyException()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), new byte[16])
                .Build());

        AssertEx.Throws<BinaryEncryptionKeyException>(
            "32-byte", () => serializer.Deserialize<int>(payload));
    }

    [Fact]
    public void WithEncryption_AnEmptyKey_ThrowsKeyException()
    {
        AssertEx.Throws<BinaryEncryptionKeyException>(
            "empty",
            () => BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), Array.Empty<byte>()));
    }

    // --- ENC-11: a key is always an owned copy ---------------------------------------------------

    [Fact]
    public void CopyFrom_TheSourceChangingAfterwards_LeavesTheKeyAsItWas()
    {
        byte[] material = NewKey();
        using var key = SecretKey.CopyFrom(material);
        byte[] copied = key.Span.ToArray();

        Array.Clear(material);

        Assert.Equal(copied, key.Span.ToArray());
        Assert.NotEqual<byte[]>(material, copied);
    }

    [Fact]
    public void Serialize_AfterTheCallersKeyArrayIsCleared_StillUsesTheKeyItCopied()
    {
        byte[] material = NewKey();
        var serializer = Encrypted(material);

        byte[] frame = serializer.Serialize(123);
        Array.Clear(material);

        Assert.Equal(123, serializer.Deserialize<int>(frame));
    }

    // --- ENC-15, ENC-16: resolved keys and temporary buffers end with their phase ----------------

    [Fact]
    public void Serialize_TheKeyResolvedForEncryption_IsDisposedWhenThePhaseEnds()
    {
        var provider = new RecordingKeyProvider(NewKey());
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), provider)
                .Build());

        serializer.Serialize(123);

        var issued = Assert.Single(provider.Issued);
        Assert.Throws<ObjectDisposedException>(() => _ = issued.Span.Length);
    }

    [Fact]
    public void Deserialize_TheKeyResolvedForDecryption_IsDisposedWhenThePhaseEnds()
    {
        var provider = new RecordingKeyProvider(NewKey());
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), provider)
                .Build());

        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));

        Assert.Equal(2, provider.Issued.Count);
        Assert.All(
            provider.Issued,
            key => Assert.Throws<ObjectDisposedException>(() => _ = key.Span.Length));
    }

    [Fact]
    public void Dispose_ASecretKey_ReleasesTheMaterialItOwned()
    {
        var key = SecretKey.CopyFrom(NewKey());

        key.Dispose();

        Assert.Equal(0, key.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = key.Span.Length);
    }

    [Fact]
    public void PooledPhaseBuffers_AreAlwaysReturnedCleared()
    {
        // A rented cipher or compression buffer holds plaintext until it goes back to the pool, and
        // nothing observes it afterwards, so the clearing is asserted where it is written.
        var returns = SourceTree.ProductionFiles
            .SelectMany(file => file.Value.Split('\n').Select(line => (File: file.Key, Line: line)))
            .Where(entry => entry.Line.Contains("ArrayPool<byte>.Shared.Return(", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(returns);
        Assert.All(returns, entry => Assert.Contains(
            "clearArray: true", entry.Line, StringComparison.Ordinal));
    }

    // --- ENC-17: a resolved key is never reused for another key id -------------------------------

    [Fact]
    public void Deserialize_ASecondPayloadNamingAnotherKeyId_DoesNotFallBackToTheResolvedOne()
    {
        byte[] primary = NewKey();
        byte[] rotated = NewKey();

        byte[] underPrimary = Encrypted(primary, "primary").Serialize(1);
        byte[] underRotated = Encrypted(rotated, "rotated").Serialize(2);

        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), id => id == "primary" ? primary : null)
                .Build());

        Assert.Equal(1, reader.Deserialize<int>(underPrimary));

        AssertEx.Throws<BinaryEncryptionKeyException>(
            "rotated", () => reader.Deserialize<int>(underRotated));

        Assert.Equal(1, reader.Deserialize<int>(underPrimary));
    }

    // --- ENC-21: both phase ceilings bound encryption in both directions -------------------------

    [Fact]
    public void Serialize_PlaintextAboveMaxCompressedBytes_ThrowsLimitBeforeEncrypting()
    {
        // What encryption receives is what compression produced, so it answers to that phase's
        // ceiling before a cipher buffer exists at all.
        var serializer = Limited(SerializationLimits.Default with { MaxCompressedBytes = 16 });

        AssertEx.Throws<BinaryLimitException>(
            "Compressed payload length", () => serializer.Serialize(new string('x', 512)));
    }

    [Fact]
    public void Serialize_CiphertextAboveMaxEncryptedBytes_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxEncryptedBytes = 16 });

        AssertEx.Throws<BinaryLimitException>(
            "could not fit within the configured maximum", () => serializer.Serialize(new string('x', 64)));
    }

    [Fact]
    public void Deserialize_ACiphertextAboveMaxEncryptedBytes_ThrowsLimit()
    {
        byte[] frame = Encrypted(Shared).Serialize(new string('x', 200));

        AssertEx.Throws<BinaryLimitException>(
            "OnDiskLength",
            () => Limited(SerializationLimits.Default with { MaxEncryptedBytes = 64 })
                .Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_ADeclaredPlaintextAboveMaxCompressedBytes_ThrowsLimit()
    {
        byte[] frame = Encrypted(Shared).Serialize(new string('x', 200));

        AssertEx.Throws<BinaryLimitException>(
            "CompressedLength",
            () => Limited(SerializationLimits.Default with { MaxCompressedBytes = 64 })
                .Deserialize<string>(frame));
    }

    // --- ENC-22: a custom algorithm travels under its registered name -----------------------------

    [Fact]
    public void Deserialize_ACustomEncryptionAlgorithm_RoundTripsUnderItsRegisteredName()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new UnauthenticatedCipher(), NewKey())
                .RegisterCustomEncryption(
                    UnauthenticatedCipher.RegisteredName, static () => new UnauthenticatedCipher())
                .Build());
        var source = new Person { Name = "Alice", Age = 30 };

        byte[] frame = serializer.Serialize(source);
        var header = Wire.ReadHeader(frame);

        Assert.Equal((byte)EncryptionAlgorithm.Custom, header.Encryption);
        Assert.Equal(UnauthenticatedCipher.RegisteredName, header.CustomEncryptionName);
        Assert.Equivalent(source, serializer.Deserialize<Person>(frame));
    }

    // --- ENC-23: a payload whose cipher cannot authenticate the header -----------------------------

    [Fact]
    public void Deserialize_APayloadEncryptedWithoutMetadataAuthentication_IsRejectedWhenEncryptionIsRequired()
    {
        // §13.1: substituting a cipher that cannot authenticate the header is the same downgrade as
        // substituting no cipher at all, so it is the payload that failed the policy, not this
        // reader's configuration.
        byte[] key = NewKey();

        byte[] downgraded = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new UnauthenticatedCipher(), key)
                .Build()).Serialize(123);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), key)
                .RequireEncryption()
                .RegisterCustomEncryption(
                    UnauthenticatedCipher.RegisteredName, static () => new UnauthenticatedCipher())
                .Build());

        AssertEx.Throws<BinaryIntegrityException>(
            UnauthenticatedCipher.RegisteredName, () => serializer.Deserialize<int>(downgraded));
    }

    [Fact]
    public void Deserialize_APayloadEncryptedWithoutMetadataAuthentication_IsReadWhenNoPolicyDemandsOne()
    {
        // The same frame under a capability rather than a policy: §21.1 leaves that read legal, which
        // is what makes the policy the thing that has to refuse it.
        byte[] key = NewKey();

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new UnauthenticatedCipher(), key)
                .RegisterCustomEncryption(
                    UnauthenticatedCipher.RegisteredName, static () => new UnauthenticatedCipher())
                .Build());

        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));
    }

    // --- P5-03: both protection policies at once ---------------------------------------------------

    [Fact]
    public void Deserialize_UnderBothProtectionPolicies_AcceptsOnlyAFullyProtectedPayload()
    {
        var protectedSerializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32())
                .WithEncryption(new Aes256Gcm(), Shared)
                .RequireChecksum()
                .RequireEncryption()
                .Build());

        byte[] encryptedOnly = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256Gcm(), Shared)
                .Build()).Serialize(123);

        byte[] checksummedOnly = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32())
                .Build()).Serialize(123);

        Assert.Equal(123, protectedSerializer.Deserialize<int>(protectedSerializer.Serialize(123)));
        Assert.Throws<BinaryIntegrityException>(
            () => protectedSerializer.Deserialize<int>(encryptedOnly));
        Assert.Throws<BinaryIntegrityException>(
            () => protectedSerializer.Deserialize<int>(checksummedOnly));
    }

    /// <summary>One key for the read-side ceilings, so writer and reader differ only in their limits.</summary>
    private static readonly byte[] Shared = NewKey();

    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), Shared)
            .WithLimits(limits)
            .Build());
}
