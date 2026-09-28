using System.Buffers;
using System.Security.Cryptography;
using ViShap.Viper.Checksum;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Algorithms;

/// <summary>
/// Pins ENC-01…ENC-05, ENC-07, ENC-09…ENC-17, ENC-19…ENC-22, ENC-24…ENC-26 and ENC-29, and CFG-05:
/// authenticated metadata, key ownership and key size, the phase ceilings encryption answers to, what
/// the service holds an algorithm to, the built-in ciphers, the frame encrypted straight into its
/// destination, and the difference between being able to decrypt and requiring it.
/// </summary>
public class EncryptionTests
{
    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static BinarySerializer Encrypted(byte[] key, string? keyId = null) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256GcmEncryption(), key, keyId)
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
                .WithEncryption(new Aes256GcmEncryption(), NewKey())
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
                .WithEncryption(new Aes256GcmEncryption(), _ => shared)
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
                .WithEncryption(new Aes256GcmEncryption(), _ => shared)
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
                .WithEncryption(new Aes256GcmEncryption(), id => { observed = id; return key; }, "primary")
                .Build());

        Assert.Equal(123, reader.Deserialize<int>(payload));
        Assert.Equal("primary", observed);
    }

    // --- ENC-02, ENC-24: a key the algorithm cannot use --------------------------------------------

    [Fact]
    public void Build_AFixedKeyOfTheWrongSizeForTheAlgorithm_ThrowsConfiguration()
    {
        AssertEx.Throws<BinaryConfigurationException>(
            "32-byte",
            () => BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256GcmEncryption(), new byte[16])
                .Build());
    }

    [Fact]
    public void Build_AFixedKeyOfTheWrongSizeWithoutEncryption_IsNotChecked()
    {
        var options = BinarySerializerOptions.Configure()
            .WithEncryption(new NoEncryption(), new byte[16])
            .Build();

        Assert.Equal(42, new BinarySerializer(options).Deserialize<int>(new BinarySerializer(options).Serialize(42)));
    }

    [Fact]
    public void Serialize_AProvidedKeyOfTheWrongSizeForTheAlgorithm_ThrowsKeyException()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256GcmEncryption(), static _ => new byte[16])
                .Build());

        AssertEx.Throws<BinaryEncryptionKeyException>("32-byte", () => serializer.Serialize(123));
    }

    [Fact]
    public void Deserialize_AProvidedKeyOfTheWrongSizeForTheAlgorithm_ThrowsKeyException()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);

        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithKeys(new byte[16])
                .Build());

        AssertEx.Throws<BinaryEncryptionKeyException>(
            "32-byte", () => serializer.Deserialize<int>(payload));
    }

    [Fact]
    public void Deserialize_AProvidedKeyOfTheWrongSize_IsClearedWhenRefused()
    {
        byte[] payload = Encrypted(NewKey()).Serialize(123);
        var keys = new RecordingKeyProvider(new byte[16]);

        var serializer = new BinarySerializer(BinarySerializerOptions.Configure().WithKeys(keys).Build());

        Assert.Throws<BinaryEncryptionKeyException>(() => serializer.Deserialize<int>(payload));
        Assert.NotEmpty(keys.Issued);
        Assert.All(keys.Issued, key => Assert.Equal(0, key.Length));
    }

    [Fact]
    public void WithEncryption_AnEmptyKey_ThrowsKeyException()
    {
        AssertEx.Throws<BinaryEncryptionKeyException>(
            "empty",
            () => BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256GcmEncryption(), Array.Empty<byte>()));
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
                .WithEncryption(new Aes256GcmEncryption(), provider)
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
                .WithEncryption(new Aes256GcmEncryption(), provider)
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
                .WithEncryption(new Aes256GcmEncryption(), id => id == "primary" ? primary : null)
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
            nameof(SerializationLimits.MaxEncryptedBytes), () => serializer.Serialize(new string('x', 64)));
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
                .WithEncryption(new Aes256GcmEncryption(), key)
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

    // --- ENC-25: the service holds an algorithm to what it states ---------------------------------------

    [Fact]
    public void Serialize_AnAlgorithmStatingACiphertextShorterThanThePlaintext_ThrowsConfiguration()
    {
        var serializer = Misbehaving(new MisbehavingCipher { LengthDelta = -5 });

        AssertEx.Throws<BinaryConfigurationException>("never shorter", () => serializer.Serialize(123));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Serialize_AnAlgorithmReportingOtherThanTheLengthItStated_ThrowsConfiguration(int writtenDelta)
    {
        var serializer = Misbehaving(new MisbehavingCipher { WrittenDelta = writtenDelta });

        AssertEx.Throws<BinaryConfigurationException>("wrote", () => serializer.Serialize(123));
    }

    [Fact]
    public void Serialize_AnAlgorithmRefusingTheDestinationItStated_ThrowsConfiguration()
    {
        var serializer = Misbehaving(new MisbehavingCipher { RefuseDestination = true });

        var exception = AssertEx.Throws<BinaryConfigurationException>("refused", () => serializer.Serialize(123));
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Deserialize_AnAlgorithmReportingAPlaintextOutsideTheCiphertext_ThrowsConfiguration(int reported)
    {
        byte[] frame = Misbehaving(new MisbehavingCipher()).Serialize(123);

        var reader = Misbehaving(new MisbehavingCipher { DecryptedLength = reported });

        AssertEx.Throws<BinaryConfigurationException>("reported", () => reader.Deserialize<int>(frame));
    }

    [Fact]
    public void Serialize_AWellBehavedCustomAlgorithm_RoundTripsThroughTheSameChecks()
    {
        var serializer = Misbehaving(new MisbehavingCipher());

        Assert.Equal(123, serializer.Deserialize<int>(serializer.Serialize(123)));
    }

    private static BinarySerializer Misbehaving(MisbehavingCipher cipher) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(cipher, Shared)
            .RegisterCustomEncryption(MisbehavingCipher.RegisteredName, () => cipher)
            .Build());

    /// <summary>
    /// A pass-through cipher that stores its plaintext behind a four-byte marker and can be told to
    /// break each promise of the algorithm interface in turn.
    /// </summary>
    private sealed class MisbehavingCipher : IEncryptionAlgorithm
    {
        public const string RegisteredName = "misbehaving";

        public int LengthDelta { get; init; }

        public int WrittenDelta { get; init; }

        public bool RefuseDestination { get; init; }

        public int? DecryptedLength { get; init; }

        public EncryptionAlgorithm Kind => EncryptionAlgorithm.Custom;

        public string? CustomName => RegisteredName;

        public bool AuthenticatesAssociatedData => true;

        public int KeySizeInBytes => 32;

        public int GetCiphertextLength(int plaintextLength) => plaintextLength + 4 + LengthDelta;

        public int Encrypt(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> associatedData,
            Span<byte> destination)
        {
            if (RefuseDestination)
                throw new ArgumentException("Destination refused.", nameof(destination));

            destination.Clear();
            plaintext[..Math.Min(plaintext.Length, Math.Max(0, destination.Length - 4))]
                .CopyTo(destination[Math.Min(4, destination.Length)..]);
            return destination.Length + WrittenDelta;
        }

        public int Decrypt(
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> associatedData,
            Span<byte> destination)
        {
            ciphertext[4..].CopyTo(destination);
            return DecryptedLength ?? ciphertext.Length - 4;
        }
    }

    // --- ENC-26: ChaCha20-Poly1305, where the platform provides it --------------------------------

    [Fact]
    public void Deserialize_ChaCha20Poly1305_RoundTripsWhereSupportedAndIsRefusedAtBuildElsewhere()
    {
        var configure = () => BinarySerializerOptions.Configure()
            .WithEncryption(new ChaCha20Poly1305Encryption(), NewKey())
            .RequireEncryption()
            .Build();

        if (!ChaCha20Poly1305.IsSupported)
        {
            AssertEx.Throws<BinaryFormatNotSupportedException>(nameof(ChaCha20Poly1305Encryption), () => configure());
            return;
        }

        var serializer = new BinarySerializer(configure());
        var source = new Person { Name = "Alice", Age = 30 };

        byte[] frame = serializer.Serialize(source);

        Assert.Equal((byte)EncryptionAlgorithm.ChaCha20Poly1305, Wire.ReadHeader(frame).Encryption);
        Assert.Equivalent(source, serializer.Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_AChaCha20Poly1305FrameWhoseCiphertextChanged_ThrowsIntegrityWhereSupported()
    {
        if (!ChaCha20Poly1305.IsSupported)
            return;

        var serializer = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithEncryption(new ChaCha20Poly1305Encryption(), NewKey())
            .Build());
        byte[] frame = serializer.Serialize(123);

        Assert.Throws<BinaryIntegrityException>(
            () => serializer.Deserialize<int>(Mutate.FlipByte(frame, frame.Length - 1)));
    }

    [Fact]
    public void Deserialize_AFrameNamingChaCha20Poly1305_IsRefusedWhereUnsupportedAndAuthenticatedElsewhere()
    {
        byte[] frame = Wire.FrameWith(
            new byte[40],
            encryption: (byte)EncryptionAlgorithm.ChaCha20Poly1305,
            uncompressedLength: 12,
            compressedLength: 12,
            onDiskLength: 40);

        var reader = new BinarySerializer(BinarySerializerOptions.Configure().WithKeys(NewKey()).Build());

        if (ChaCha20Poly1305.IsSupported)
            Assert.Throws<BinaryIntegrityException>(() => reader.Deserialize<int>(frame));
        else
            AssertEx.Throws<BinaryFormatNotSupportedException>(
                nameof(ChaCha20Poly1305Encryption), () => reader.Deserialize<int>(frame));
    }

    [Fact]
    public void Encrypt_ChaCha20Poly1305_StatesItsLengthAndKeySize()
    {
        var algorithm = new ChaCha20Poly1305Encryption();

        Assert.Equal(128 + 28, algorithm.GetCiphertextLength(128));
        Assert.Equal(32, algorithm.KeySizeInBytes);
        Assert.True(algorithm.AuthenticatesAssociatedData);
    }

    // --- ENC-29: an encrypted frame is encrypted straight into the destination ---------------------

    [Fact]
    public void Serialize_AnEncryptedFrameToABufferWriter_AsksOnceForTheWholeFrameAndAdvancesOnce()
    {
        var serializer = Encrypted(Shared);
        var destination = new RecordingBufferWriter();

        serializer.Serialize(destination, "a value");

        int frameLength = destination.Written.Length;
        Assert.Equal([frameLength], destination.SizeHints);
        Assert.Equal([frameLength], destination.Advances);
        Assert.Equal("a value", serializer.Deserialize<string>(destination.Written));
    }

    [Fact]
    public void Serialize_AnEncryptedFrameToAWriterHandingOutShortSpans_StillWritesTheWholeFrame()
    {
        var serializer = Encrypted(Shared);
        var destination = new StingyBufferWriter(7);

        serializer.Serialize(destination, "a value");

        Assert.Equal("a value", serializer.Deserialize<string>(destination.Written));
    }

    [Fact]
    public void Serialize_AnEncryptedFrameWhoseCipherFails_LeavesTheBufferWriterEmpty()
    {
        var serializer = Misbehaving(new MisbehavingCipher { WrittenDelta = 1 });
        var destination = new ArrayBufferWriter<byte>();

        Assert.Throws<BinaryConfigurationException>(() => serializer.Serialize(destination, 123));

        Assert.Equal(0, destination.WrittenCount);
    }

    [Fact]
    public void Serialize_AnEncryptedFrame_HasTheLengthTheAlgorithmStated()
    {
        byte[] frame = Encrypted(Shared).Serialize(new string('x', 300));
        var header = Wire.ReadHeader(frame);

        Assert.Equal(new Aes256GcmEncryption().GetCiphertextLength(header.CompressedLength), header.OnDiskLength);
        Assert.Equal(header.HeaderLength + header.OnDiskLength, frame.Length);
    }

    /// <summary>Records every span request and every advance, and keeps what was committed.</summary>
    private sealed class RecordingBufferWriter : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _inner = new();

        public List<int> SizeHints { get; } = [];

        public List<int> Advances { get; } = [];

        public byte[] Written => _inner.WrittenSpan.ToArray();

        public void Advance(int count)
        {
            Advances.Add(count);
            _inner.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            SizeHints.Add(sizeHint);
            return _inner.GetMemory(sizeHint);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            SizeHints.Add(sizeHint);
            return _inner.GetSpan(sizeHint);
        }
    }

    // --- P5-03: both protection policies at once ---------------------------------------------------

    [Fact]
    public void Deserialize_UnderBothProtectionPolicies_AcceptsOnlyAFullyProtectedPayload()
    {
        var protectedSerializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32Checksum())
                .WithEncryption(new Aes256GcmEncryption(), Shared)
                .RequireChecksum()
                .RequireEncryption()
                .Build());

        byte[] encryptedOnly = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithEncryption(new Aes256GcmEncryption(), Shared)
                .Build()).Serialize(123);

        byte[] checksummedOnly = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithChecksum(new Crc32Checksum())
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
            .WithEncryption(new Aes256GcmEncryption(), Shared)
            .WithLimits(limits)
            .Build());
}
