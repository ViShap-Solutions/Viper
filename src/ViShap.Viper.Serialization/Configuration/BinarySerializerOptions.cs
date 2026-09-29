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
/// Reading a version 1 payload is self-describing: its header names the algorithms it needs, and the
/// serializer resolves them from this configuration. Reading a payload produced with different
/// settings works as long as the algorithms it names are available here. A version 0 payload names
/// nothing, so what applies to it is decided entirely by this configuration.
/// </para>
/// <example>
/// <code>
/// var options = BinarySerializerOptions.Configure()
///     .WithCompression(new BrotliCompression())
///     .WithChecksum(new Crc32Checksum())
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
    /// Supplies key material for encrypted payloads — the keys given with <c>WithEncryption</c> or with
    /// <c>WithKeys</c>. The provider always hands out owned copies, so the serializer never clears a
    /// buffer you own.
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
    /// Whether bytes without the format magic number are read as a version 0 payload instead of being
    /// rejected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A version 0 payload carries no header, so nothing in the bytes says what they are. This opt-in
    /// is what separates a deliberate compact payload from unrelated data, and it governs reading
    /// only: writing version 0 is selected with the write version. It cannot be combined with
    /// <see cref="RequireEncryption"/> or <see cref="RequireChecksum"/>, since such a payload carries
    /// neither.
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
    public bool AllowV0Fallback { get; internal init; }

    /// <summary>
    /// Whether unencrypted payloads are rejected. Without this, configuring encryption only means the
    /// serializer <em>can</em> decrypt, not that it demands encryption.
    /// </summary>
    public bool RequireEncryption { get; internal init; }

    /// <summary>Whether payloads carrying no checksum are rejected.</summary>
    public bool RequireChecksum { get; internal init; }

    internal AlgorithmCatalog Catalog { get; init; } = AlgorithmCatalog.BuiltIn;
}
