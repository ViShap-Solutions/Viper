namespace ViShap.Viper;

/// <summary>
/// The immutable configuration of one <see cref="BinarySerializer"/>: algorithms, key material,
/// resource limits and policies.
/// </summary>
/// <remarks>
/// <para>
/// Build options with <see cref="Configure"/>, which validates them once. There are no public
/// setters, so a configuration that never passed validation cannot exist.
/// </para>
/// <para>
/// Options carry the algorithm <em>primitives</em> only. Nothing you supply here takes part in
/// enforcing a resource limit — the serializer applies those around your algorithm — so a custom
/// compressor or cipher cannot weaken the protections that bound untrusted input.
/// </para>
/// <para>
/// Reading is self-describing: a payload's header names the algorithms it needs, and the serializer
/// resolves them from this configuration. Reading a payload produced with different settings works as
/// long as the algorithms it names are available here.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithCompression(new Brotli())
///     .WithChecksum(new Crc32())
///     .PreserveReferences()
///     .Build();
///
/// var serializer = new BinarySerializer(options);
/// </code>
/// </example>
/// </remarks>
public sealed record BinarySerializerOptions
{
    internal BinarySerializerOptions() { }

    /// <summary>
    /// The default configuration: format version 1, no compression, no checksum, no encryption,
    /// default limits.
    /// </summary>
    public static BinarySerializerOptions Default { get; } = new()
    {
        Limits = SerializationLimits.Default,
        Catalog = AlgorithmCatalog.BuiltIn
    };

    /// <summary>Starts building a configuration.</summary>
    /// <returns>A builder; call <see cref="BinarySerializerOptionsBuilder.Build"/> when done.</returns>
    public static BinarySerializerOptionsBuilder Configure() => new();

    /// <summary>The compression applied to payloads this serializer writes.</summary>
    public ICompressionAlgorithm Compression { get; internal init; } = new NoCompression();

    /// <summary>The checksum stored with payloads this serializer writes.</summary>
    public IChecksumAlgorithm Checksum { get; internal init; } = new NoChecksum();

    /// <summary>The encryption applied to payloads this serializer writes.</summary>
    public IEncryptionAlgorithm Encryption { get; internal init; } = new NoEncryption();

    /// <summary>
    /// Supplies key material for encrypted payloads. The provider always hands out owned copies, so
    /// the serializer never clears a buffer you own.
    /// </summary>
    public IKeyProvider? Keys { get; internal init; }

    /// <summary>
    /// The key id recorded in headers this serializer writes, so a reader can select the right key.
    /// Not secret.
    /// </summary>
    public string? KeyId { get; internal init; }

    /// <summary>The format version used for writing. Reading detects the version from the payload.</summary>
    public int WriteVersion { get; internal init; } = BinaryFormatConstants.LatestVersion;

    /// <summary>
    /// Whether object identity is preserved: a graph that shares an object encodes it once and refers
    /// back to it, and cycles become representable.
    /// </summary>
    /// <remarks>
    /// Costs five bytes per structural reference value. Without it a cycle is rejected with
    /// <see cref="BinaryTypeException"/> and a shared object is written — and read back — twice.
    /// </remarks>
    public bool PreserveReferences { get; internal init; }

    /// <summary>The resource policy applied to every operation.</summary>
    public SerializationLimits Limits { get; internal init; } = SerializationLimits.Default;

    /// <summary>
    /// Whether a stream without the format magic number is read as a legacy version 0 payload instead
    /// of being rejected.
    /// </summary>
    public bool AllowV0Fallback { get; internal init; }

    /// <summary>
    /// Whether unencrypted payloads are rejected. Without this, configuring encryption only means the
    /// serializer <em>can</em> decrypt, not that it demands encryption.
    /// </summary>
    public bool RequireEncryption { get; internal init; }

    /// <summary>Whether payloads carrying no checksum are rejected.</summary>
    public bool RequireChecksum { get; internal init; }

    internal AlgorithmCatalog Catalog { get; init; } = AlgorithmCatalog.BuiltIn;

    /// <summary>
    /// Builds options able to read a payload described by <paramref name="info"/>.
    /// </summary>
    /// <remarks>
    /// Use this to read a payload whose settings you learn at runtime, for example after
    /// <see cref="BinaryFormatInspector.Peek(Stream)"/>. Only built-in algorithms are resolved; a
    /// payload naming a custom algorithm needs options built with the matching registration.
    /// </remarks>
    /// <param name="info">Header metadata of the payload.</param>
    /// <param name="keys">Key material, required when the payload is encrypted.</param>
    /// <param name="limits">Resource policy, or <see langword="null"/> for the defaults.</param>
    /// <returns>Options configured for that payload.</returns>
    public static BinarySerializerOptions FromHeader(
        BinaryHeaderInfo info,
        IKeyProvider? keys = null,
        SerializationLimits? limits = null)
    {
        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        var catalog = AlgorithmCatalog.BuiltIn;

        return new BinarySerializerOptions
        {
            Compression = catalog.ResolveCompression(info.Compression, info.CustomCompressionName),
            Checksum = catalog.ResolveChecksum(info.ChecksumAlgorithm, info.CustomChecksumName),
            Encryption = catalog.ResolveEncryption(info.Encryption, info.CustomEncryptionName),
            Keys = keys,
            KeyId = info.KeyId,
            Limits = actualLimits,
            Catalog = catalog
        };
    }

    /// <summary>Builds options for a payload, using a single key.</summary>
    /// <param name="info">Header metadata of the payload.</param>
    /// <param name="key">Key bytes, copied immediately; pass <see langword="null"/> when the payload is not encrypted.</param>
    /// <param name="limits">Resource policy, or <see langword="null"/> for the defaults.</param>
    /// <returns>Options configured for that payload.</returns>
    public static BinarySerializerOptions FromHeader(
        BinaryHeaderInfo info,
        byte[]? key,
        SerializationLimits? limits = null) =>
        FromHeader(
            info,
            key is null or [] ? null : new StaticKeyProvider(key, info.KeyId),
            limits);

    /// <summary>Builds options for a payload, resolving the key by id.</summary>
    /// <param name="info">Header metadata of the payload.</param>
    /// <param name="keyResolver">Returns the key for the id the payload names.</param>
    /// <param name="limits">Resource policy, or <see langword="null"/> for the defaults.</param>
    /// <returns>Options configured for that payload.</returns>
    public static BinarySerializerOptions FromHeader(
        BinaryHeaderInfo info,
        Func<string?, byte[]?> keyResolver,
        SerializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(keyResolver);
        return FromHeader(info, new DelegateKeyProvider(keyResolver), limits);
    }

    /// <summary>
    /// Inspects <paramref name="stream"/> and builds options able to read it, using a single key.
    /// </summary>
    /// <param name="stream">A seekable stream positioned at the start of a payload. Its position is restored.</param>
    /// <param name="key">Key bytes, copied immediately; pass <see langword="null"/> when the payload is not encrypted.</param>
    /// <param name="limits">Resource policy, or <see langword="null"/> for the defaults.</param>
    /// <returns>Options configured for that payload.</returns>
    /// <exception cref="BinaryFormatException">The stream does not start with a recognized header.</exception>
    public static BinarySerializerOptions FromStream(
        Stream stream,
        byte[]? key = null,
        SerializationLimits? limits = null) =>
        FromHeader(Inspect(stream, limits), key, limits);

    /// <summary>Inspects <paramref name="stream"/> and builds options, resolving the key by id.</summary>
    /// <param name="stream">A seekable stream positioned at the start of a payload. Its position is restored.</param>
    /// <param name="keyResolver">Returns the key for the id the payload names.</param>
    /// <param name="limits">Resource policy, or <see langword="null"/> for the defaults.</param>
    /// <returns>Options configured for that payload.</returns>
    public static BinarySerializerOptions FromStream(
        Stream stream,
        Func<string?, byte[]?> keyResolver,
        SerializationLimits? limits = null) =>
        FromHeader(Inspect(stream, limits), keyResolver, limits);

    /// <summary>Inspects <paramref name="stream"/> and builds options, using a key provider.</summary>
    /// <param name="stream">A seekable stream positioned at the start of a payload. Its position is restored.</param>
    /// <param name="keys">Key material, required when the payload is encrypted.</param>
    /// <param name="limits">Resource policy, or <see langword="null"/> for the defaults.</param>
    /// <returns>Options configured for that payload.</returns>
    public static BinarySerializerOptions FromStream(
        Stream stream,
        IKeyProvider? keys,
        SerializationLimits? limits = null) =>
        FromHeader(Inspect(stream, limits), keys, limits);

    private static BinaryHeaderInfo Inspect(Stream stream, SerializationLimits? limits)
    {
        var actualLimits = limits ?? SerializationLimits.Default;
        actualLimits.Validate();

        return BinaryFormatInspector.Peek(stream, actualLimits)
               ?? throw new BinaryFormatException(
                   "Unable to inspect the stream header. The format is unknown or unsupported.");
    }
}
