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
///     .WithCompression(new Brotli())
///     .WithEncryption(new Aes256Gcm(), key, keyId: "2026-q3")
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
    private IKeyProvider? _keys;
    private string? _keyId;
    private int _writeVersion = BinaryFormatConstants.LatestVersion;
    private bool _preserveReferences;
    private SerializationLimits _limits = SerializationLimits.Default;
    private bool _allowV0Fallback;
    private bool _requireEncryption;
    private bool _requireChecksum;

    internal BinarySerializerOptionsBuilder() { }

    /// <summary>Compresses payloads with <paramref name="compression"/>.</summary>
    /// <param name="compression">The algorithm, for example <see cref="Brotli"/> or <see cref="Deflate"/>.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithCompression(ICompressionAlgorithm compression)
    {
        _compression = compression ?? throw new ArgumentNullException(nameof(compression));
        return this;
    }

    /// <summary>Stores a checksum with each payload and verifies it on read.</summary>
    /// <param name="checksum">The algorithm, for example <see cref="Crc32"/>.</param>
    /// <returns>The same builder.</returns>
    public BinarySerializerOptionsBuilder WithChecksum(IChecksumAlgorithm checksum)
    {
        _checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
        return this;
    }

    /// <summary>Encrypts payloads with a fixed key.</summary>
    /// <remarks>The key bytes are copied; the array you pass stays yours and is never modified.</remarks>
    /// <param name="encryption">The algorithm, for example <see cref="Aes256Gcm"/>.</param>
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
        _keys = new StaticKeyProvider(key, keyId);
        _keyId = keyId;
        return this;
    }

    /// <summary>Encrypts payloads with a key looked up by id, which is how key rotation is supported.</summary>
    /// <remarks>Whatever the resolver returns is copied immediately and never modified.</remarks>
    /// <param name="encryption">The algorithm, for example <see cref="Aes256Gcm"/>.</param>
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
        _keys = new DelegateKeyProvider(keyResolver);
        _keyId = keyId;
        return this;
    }

    /// <summary>Encrypts payloads with keys from a provider of your own.</summary>
    /// <param name="encryption">The algorithm, for example <see cref="Aes256Gcm"/>.</param>
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
        _keys = keys;
        _keyId = keyId;
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
    /// under version 0, but writing one needs a seekable destination stream.
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
    /// Reads a stream without the format magic number as a version 0 payload instead of rejecting it.
    /// </summary>
    /// <remarks>
    /// A version 0 payload has no header to recognize, so only this opt-in separates one from
    /// unrelated bytes. It affects reading alone; writing version 0 is selected with
    /// <see cref="WithVersion"/>. Because such a payload carries no protection,
    /// <see cref="Build"/> refuses this together with <see cref="RequireEncryption"/> or
    /// <see cref="RequireChecksum"/>.
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
    /// format metadata; a checksum is required but not configured; or a protection policy is
    /// combined with format version 0, which has no header in which to carry protection.
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

        if (encryption.Kind != EncryptionAlgorithm.None && _keys is null)
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
            Keys = _keys,
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
