namespace ViShap.Viper;

/// <summary>
/// Builds a <see cref="BinarySerializerOptions"/>. Obtained from
/// <see cref="BinarySerializerOptions.Configure"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every method returns the same builder, so calls chain. <see cref="Build"/> validates the result
/// once; nothing downstream re-validates it, and nothing can modify it afterwards.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithCompression(new BrotliCompression())
///     .WithEncryption(new Aes256GcmEncryption(), key, keyId: "2026-q3")
///     .RequireEncryption()
///     .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 4 * 1024 * 1024 })
///     .Build();
/// </code>
/// </example>
/// </remarks>
public sealed class BinarySerializerOptionsBuilder
{
    private readonly Dictionary<string, Func<ICompressionAlgorithm>> _customCompression = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IChecksumAlgorithm>> _customChecksum = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<IEncryptionAlgorithm>> _customEncryption = new(StringComparer.Ordinal);

    private ICompressionAlgorithm? _compression;
    private IChecksumAlgorithm? _checksum;
    private IEncryptionAlgorithm? _encryption;
    private IKeyProvider? _encryptionKeys;
    private IKeyProvider? _readKeys;
    private int? _staticKeyLength;
    private string? _keyId;
    private int _writeVersion = BinaryFormatConstants.LatestVersion;
    private bool _preserveReferences;
    private SerializationLimits _limits = SerializationLimits.Default;
    private bool _allowV0Fallback;
    private bool _requireEncryption;
    private bool _requireChecksum;

    internal BinarySerializerOptionsBuilder() { }

    /// <summary>Compresses payloads with <paramref name="compression"/>.</summary>
    /// <param name="compression">The algorithm, for example <see cref="BrotliCompression"/> or <see cref="DeflateCompression"/>.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithCompression(ICompressionAlgorithm compression)
    {
        _compression = compression ?? throw new ArgumentNullException(nameof(compression));
        return this;
    }

    /// <summary>Stores a checksum with each payload and verifies it on read.</summary>
    /// <param name="checksum">The algorithm, for example <see cref="Crc32Checksum"/> or <see cref="XxHash3Checksum"/>.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithChecksum(IChecksumAlgorithm checksum)
    {
        _checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
        return this;
    }

    /// <summary>Encrypts payloads with a fixed key.</summary>
    /// <remarks>
    /// The key bytes are copied; the array you pass stays yours and is never modified. <see cref="Build"/>
    /// checks the key's length against the algorithm's <see cref="IEncryptionAlgorithm.KeySizeInBytes"/>.
    /// </remarks>
    /// <param name="encryption">The algorithm, for example <see cref="Aes256GcmEncryption"/>.</param>
    /// <param name="key">Key material to copy.</param>
    /// <param name="keyId">
    /// Recorded in the header so a reader can select this key, and checked when reading. Supply one as
    /// soon as more than one key is in circulation.
    /// </param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithEncryption(
        IEncryptionAlgorithm encryption,
        ReadOnlySpan<byte> key,
        string? keyId = null)
    {
        ArgumentNullException.ThrowIfNull(encryption);

        _encryption = encryption;
        _encryptionKeys = new StaticKeyProvider(key, keyId);
        _staticKeyLength = key.Length;
        _keyId = keyId;
        return this;
    }

    /// <summary>Encrypts payloads with a key looked up by id, which is how key rotation is supported.</summary>
    /// <remarks>Whatever the resolver returns is copied immediately and never modified.</remarks>
    /// <param name="encryption">The algorithm, for example <see cref="Aes256GcmEncryption"/>.</param>
    /// <param name="keyResolver">Returns the key for a given id, or <see langword="null"/> when it has none.</param>
    /// <param name="keyId">The id recorded in payloads this serializer writes.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithEncryption(
        IEncryptionAlgorithm encryption,
        Func<string?, byte[]?> keyResolver,
        string? keyId = null)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(keyResolver);

        _encryption = encryption;
        _encryptionKeys = new DelegateKeyProvider(keyResolver);
        _staticKeyLength = null;
        _keyId = keyId;
        return this;
    }

    /// <summary>Encrypts payloads with keys from a provider of your own.</summary>
    /// <param name="encryption">The algorithm, for example <see cref="Aes256GcmEncryption"/>.</param>
    /// <param name="keys">The key source.</param>
    /// <param name="keyId">The id recorded in payloads this serializer writes.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithEncryption(
        IEncryptionAlgorithm encryption,
        IKeyProvider keys,
        string? keyId = null)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(keys);

        _encryption = encryption;
        _encryptionKeys = keys;
        _staticKeyLength = null;
        _keyId = keyId;
        return this;
    }

    /// <summary>Supplies a fixed key for reading encrypted payloads, without encrypting what is written.</summary>
    /// <remarks>
    /// <para>
    /// A version 1 frame names its own algorithms, so a reader needs no algorithm of its own — only the
    /// key. The key bytes are copied; the array you pass stays yours and is never modified.
    /// </para>
    /// <para>
    /// Keys have one place: <see cref="Build"/> rejects keys supplied both here and through
    /// <c>WithEncryption</c>, which already supplies them for reading as well as writing.
    /// </para>
    /// </remarks>
    /// <param name="key">Key material to copy.</param>
    /// <param name="keyId">
    /// The id the key is known by. When set, a payload naming a different id is refused rather than
    /// decrypted with the wrong key.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <exception cref="BinaryEncryptionKeyException"><paramref name="key"/> is empty.</exception>
    /// <example>
    /// <code>
    /// var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithKeys(key, keyId: "2026-q3")
    ///     .Build());
    /// Order? order = reader.Deserialize&lt;Order&gt;(bytes);   // any V1 frame, encrypted or not
    /// </code>
    /// </example>
    public BinarySerializerOptionsBuilder WithKeys(ReadOnlySpan<byte> key, string? keyId = null)
    {
        _readKeys = new StaticKeyProvider(key, keyId);
        return this;
    }

    /// <summary>
    /// Supplies keys for reading encrypted payloads, looked up by the key id each payload names,
    /// without encrypting what is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A version 1 frame names its own algorithms, so a reader needs no algorithm of its own — only the
    /// key. Whatever the resolver returns is copied immediately and never modified. This is how one
    /// reader follows key rotation: it reads payloads encrypted under any key the resolver can still
    /// produce.
    /// </para>
    /// <para>
    /// Keys have one place: <see cref="Build"/> rejects keys supplied both here and through
    /// <c>WithEncryption</c>, which already supplies them for reading as well as writing.
    /// </para>
    /// </remarks>
    /// <param name="keyResolver">
    /// Returns the key for the id a payload names — the id is <see langword="null"/> when the payload
    /// names none — or <see langword="null"/> when it has no key for it.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyResolver"/> is null.</exception>
    /// <example>
    /// <code>
    /// var reader = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithKeys(keyId => vault.Get(keyId))
    ///     .Build());
    /// Order? order = reader.Deserialize&lt;Order&gt;(stream);   // any V1 frame, encrypted or not
    /// </code>
    /// </example>
    public BinarySerializerOptionsBuilder WithKeys(Func<string?, byte[]?> keyResolver)
    {
        ArgumentNullException.ThrowIfNull(keyResolver);

        _readKeys = new DelegateKeyProvider(keyResolver);
        return this;
    }

    /// <summary>
    /// Supplies keys for reading encrypted payloads from a provider of your own, without encrypting
    /// what is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A version 1 frame names its own algorithms, so a reader needs no algorithm of its own — only the
    /// key, which the provider resolves by the key id each payload names.
    /// </para>
    /// <para>
    /// Keys have one place: <see cref="Build"/> rejects keys supplied both here and through
    /// <c>WithEncryption</c>, which already supplies them for reading as well as writing.
    /// </para>
    /// </remarks>
    /// <param name="keys">The key source.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is null.</exception>
    public BinarySerializerOptionsBuilder WithKeys(IKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _readKeys = keys;
        return this;
    }

    /// <summary>Selects the wire format version used for writing. Reading always detects it from the payload.</summary>
    /// <remarks>
    /// Selecting version 0 is enough to write the headerless format; <see cref="AllowV0Fallback"/>
    /// is a separate, read-side choice. An unsupported version is rejected by <see cref="Build"/>.
    /// </remarks>
    /// <param name="version">
    /// 1 for the self-describing envelope, or 0 for the compact headerless one, which carries no
    /// reference framing and no compression, checksum or encryption, since a headerless payload has
    /// nowhere to record them, which also makes version 0 incompatible with
    /// <see cref="RequireEncryption"/> and <see cref="RequireChecksum"/>. Keyed contracts do work
    /// under version 0 as well, with any destination.
    /// </param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithVersion(int version)
    {
        _writeVersion = version;
        return this;
    }

    /// <summary>
    /// Preserves object identity: a shared object is encoded once, and cycles become representable.
    /// </summary>
    /// <remarks>
    /// Costs five bytes per structural reference value. Without it, a cycle is rejected with
    /// <see cref="BinaryTypeException"/> and shared objects are duplicated on read.
    /// </remarks>
    /// <param name="preserve">Whether to preserve identity.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder PreserveReferences(bool preserve = true)
    {
        _preserveReferences = preserve;
        return this;
    }

    /// <summary>Sets the resource policy applied to every operation.</summary>
    /// <param name="limits">The limits; derive them from <see cref="SerializationLimits.Default"/> with <c>with</c>.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithLimits(SerializationLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        return this;
    }

    /// <summary>
    /// Reads bytes without the format magic number as a version 0 payload instead of rejecting them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A version 0 payload has no header to recognize, so only this opt-in separates one from
    /// unrelated bytes. It affects reading alone; writing version 0 is selected with
    /// <see cref="WithVersion"/>. Because such a payload carries no protection,
    /// <see cref="Build"/> refuses this together with <see cref="RequireEncryption"/> or
    /// <see cref="RequireChecksum"/>.
    /// </para>
    /// <para>
    /// A version 0 payload is read synchronously, from a span, a sequence or a seekable stream; an
    /// asynchronous read that meets one raises <see cref="NotSupportedException"/>.
    /// V0 carries neither a magic number nor a length: it is a codec for protocols that already frame
    /// their messages — a length prefix, a message type, a channel. The protocol knows where a message
    /// ends, so the caller already holds one message's bytes and reads them synchronously. Waiting
    /// asynchronously is for a reader that does not know where the message ends; with V0 the protocol
    /// knows, not Viper.
    /// </para>
    /// <example>
    /// <code>
    /// // V1 from a socket: Viper knows the frame boundary — the header carries the length
    /// Order? order = await serializer.DeserializeAsync&lt;Order&gt;(networkStream, cancellationToken);
    ///
    /// // V0 inside your own protocol: the protocol knows the frame boundary
    /// var compact = new BinarySerializer(BinarySerializerOptions.Configure()
    ///     .WithVersion(0).AllowV0Fallback().Build());
    ///
    /// while (true)
    /// {
    ///     ReadResult read = await pipe.ReadAsync(cancellationToken);
    ///     ReadOnlySequence&lt;byte&gt; buffer = read.Buffer;
    ///
    ///     // the protocol: a 4-byte little-endian length, then the V0 payload
    ///     while (TryReadFrame(ref buffer, out ReadOnlySequence&lt;byte&gt; frame))
    ///     {
    ///         Order? message = compact.Deserialize&lt;Order&gt;(frame);   // synchronous: the frame is in memory
    ///         Handle(message);
    ///     }
    ///
    ///     pipe.AdvanceTo(buffer.Start, buffer.End);
    ///     if (read.IsCompleted) break;
    /// }
    ///
    /// static bool TryReadFrame(ref ReadOnlySequence&lt;byte&gt; buffer, out ReadOnlySequence&lt;byte&gt; frame)
    /// {
    ///     var reader = new SequenceReader&lt;byte&gt;(buffer);
    ///     if (!reader.TryReadLittleEndian(out int length) || reader.Remaining &lt; length)
    ///     {
    ///         frame = default;
    ///         return false;
    ///     }
    ///
    ///     frame = buffer.Slice(reader.Position, length);
    ///     buffer = buffer.Slice(frame.End);
    ///     return true;
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="allow">Whether headerless input is accepted.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder AllowV0Fallback(bool allow = true)
    {
        _allowV0Fallback = allow;
        return this;
    }

    /// <summary>Rejects unencrypted payloads instead of reading them as plaintext.</summary>
    /// <remarks>
    /// Configuring encryption alone only means the serializer <em>can</em> decrypt. Without this
    /// policy an attacker can replace an encrypted message with a plaintext one and still be read.
    /// Requires an algorithm that authenticates format metadata, and rules out format version 0 on
    /// both sides: <see cref="Build"/> rejects it together with <see cref="WithVersion"/><c>(0)</c>
    /// or <see cref="AllowV0Fallback"/>, because a headerless payload has no metadata to protect.
    /// </remarks>
    /// <param name="require">Whether encryption is mandatory.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder RequireEncryption(bool require = true)
    {
        _requireEncryption = require;
        return this;
    }

    /// <summary>Rejects payloads that carry no checksum.</summary>
    /// <remarks>
    /// Like <see cref="RequireEncryption"/> this rules out format version 0 on both sides, since a
    /// headerless payload carries no checksum to verify.
    /// </remarks>
    /// <param name="require">Whether a checksum is mandatory.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder RequireChecksum(bool require = true)
    {
        _requireChecksum = require;
        return this;
    }

    /// <summary>
    /// Registers a custom compression algorithm under <paramref name="name"/>, the name payloads
    /// carry.
    /// </summary>
    /// <remarks>
    /// Registration is per configuration, not global, so one part of an application cannot change what
    /// another part uses. Register before reading a payload that names the algorithm.
    /// </remarks>
    /// <param name="name">The name recorded in headers.</param>
    /// <param name="factory">Creates the algorithm; called once per resolution.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="factory"/> is null.</exception>
    public BinarySerializerOptionsBuilder RegisterCustomCompression(
        string name, Func<ICompressionAlgorithm> factory) =>
        Register(_customCompression, name, factory);

    /// <summary>Registers a custom checksum algorithm under <paramref name="name"/>.</summary>
    /// <param name="name">The name recorded in headers.</param>
    /// <param name="factory">Creates the algorithm; called once per resolution.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="factory"/> is null.</exception>
    public BinarySerializerOptionsBuilder RegisterCustomChecksum(
        string name, Func<IChecksumAlgorithm> factory) =>
        Register(_customChecksum, name, factory);

    /// <summary>Registers a custom encryption algorithm under <paramref name="name"/>.</summary>
    /// <param name="name">The name recorded in headers.</param>
    /// <param name="factory">Creates the algorithm; called once per resolution.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="factory"/> is null.</exception>
    public BinarySerializerOptionsBuilder RegisterCustomEncryption(
        string name, Func<IEncryptionAlgorithm> factory) =>
        Register(_customEncryption, name, factory);

    /// <summary>Validates the configuration and produces the options.</summary>
    /// <returns>An immutable configuration.</returns>
    /// <exception cref="BinaryConfigurationException">
    /// A limit is not positive; the write version is not a supported wire format; encryption is
    /// required but not configured, or is configured with an algorithm that cannot authenticate
    /// format metadata; a checksum is required but not configured; a protection policy is combined
    /// with format version 0, which has no header in which to carry protection; keys are supplied
    /// both through <c>WithEncryption</c> and through <c>WithKeys</c>; or the fixed key given to
    /// <c>WithEncryption</c> is not the length the algorithm requires.
    /// </exception>
    /// <exception cref="BinaryFormatNotSupportedException">
    /// Payloads are to be encrypted with <see cref="ChaCha20Poly1305Encryption"/> on a platform that does
    /// not provide it.
    /// </exception>
    public BinarySerializerOptions Build()
    {
        _limits.Validate();

        if (_writeVersion is < V0FormatPipeline.Version or > BinaryFormatConstants.LatestVersion)
            throw new BinaryConfigurationException(
                $"Format version {_writeVersion} cannot be written. Supported versions are " +
                $"{V0FormatPipeline.Version} through {BinaryFormatConstants.LatestVersion}.");

        var encryption = _encryption ?? new NoEncryption();

        if (_requireEncryption && encryption.Kind == EncryptionAlgorithm.None)
            throw new BinaryConfigurationException(
                "RequireEncryption is set, but no encryption algorithm is configured.");

        if (_requireEncryption && !encryption.AuthenticatesAssociatedData)
            throw new BinaryConfigurationException(
                $"RequireEncryption is set, but '{encryption.GetType().Name}' does not authenticate " +
                "format metadata, so header tampering would go undetected.");

        if (_encryptionKeys is not null && _readKeys is not null)
            throw new BinaryConfigurationException(
                "Keys are supplied both through WithEncryption and through WithKeys. WithEncryption " +
                "already supplies its keys for reading as well as writing; keep one of the two.");

        var keys = _encryptionKeys ?? _readKeys;

        if (encryption.Kind != EncryptionAlgorithm.None
            && _staticKeyLength is int keyLength
            && keyLength != encryption.KeySizeInBytes)
            throw new BinaryConfigurationException(
                $"'{encryption.GetType().Name}' requires a {encryption.KeySizeInBytes}-byte key, but the " +
                $"key given to WithEncryption is {keyLength} byte(s).");

        if (encryption is ChaCha20Poly1305Encryption)
            ChaCha20Poly1305Encryption.EnsureSupported();

        if (encryption.Kind != EncryptionAlgorithm.None && keys is null)
            throw new BinaryConfigurationException(
                "An encryption algorithm is configured, but no key material was supplied.");

        if (_requireChecksum && (_checksum?.Kind ?? ChecksumAlgorithm.None) == ChecksumAlgorithm.None)
            throw new BinaryConfigurationException(
                "RequireChecksum is set, but no checksum algorithm is configured.");

        RejectHeaderlessProtection(_requireEncryption, nameof(RequireEncryption));
        RejectHeaderlessProtection(_requireChecksum, nameof(RequireChecksum));

        return new BinarySerializerOptions
        {
            Compression = _compression ?? new NoCompression(),
            Checksum = _checksum ?? new NoChecksum(),
            Encryption = encryption,
            Keys = keys,
            KeyId = _keyId,
            WriteVersion = _writeVersion,
            PreserveReferences = _preserveReferences,
            Limits = _limits,
            AllowV0Fallback = _allowV0Fallback,
            RequireEncryption = _requireEncryption,
            RequireChecksum = _requireChecksum,
            Catalog = AlgorithmCatalog.Create(_customCompression, _customChecksum, _customEncryption)
        };
    }

    /// <summary>
    /// Refuses a protection policy that a headerless payload could never satisfy, on whichever side
    /// of the operation version 0 was allowed in.
    /// </summary>
    private void RejectHeaderlessProtection(bool policyRequested, string policy)
    {
        if (!policyRequested)
            return;

        if (_writeVersion == V0FormatPipeline.Version)
            throw new BinaryConfigurationException(
                $"{policy} is set, but format version 0 is selected for writing. A headerless " +
                "payload has no metadata to record protection, so the data would be written " +
                "unprotected. Write version 1, or drop the policy.");

        if (_allowV0Fallback)
            throw new BinaryConfigurationException(
                $"{policy} is set together with AllowV0Fallback. A headerless payload carries no " +
                "protection, so reading one would return unprotected data under a policy that " +
                "forbids it. Drop the fallback, or drop the policy.");
    }

    private BinarySerializerOptionsBuilder Register<T>(
        Dictionary<string, Func<T>> registrations,
        string name,
        Func<T> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        registrations[name] = factory;
        return this;
    }
}
