namespace ViShap.Viper;

/// <summary>
/// Serializes objects to a compact binary form and reads them back.
/// </summary>
/// <remarks>
/// <para>
/// An instance is immutable once constructed and safe to share across threads; each call gets its own
/// resource accounting. Create one per configuration and reuse it — the type caches per-type metadata,
/// so a long-lived instance is faster than a fresh one per call.
/// </para>
/// <para>
/// Payloads are self-describing: the header records the format version and the algorithms used, so a
/// reader configured differently still knows how to unwrap the data. Reading therefore needs a
/// seekable stream, since the version is inspected before anything is consumed.
/// </para>
/// <para>
/// Deserialization of untrusted input is bounded by <see cref="Security.SerializationLimits"/>. A
/// payload that exceeds a limit raises <see cref="Exceptions.BinaryLimitException"/> before the work
/// it asked for is performed.
/// </para>
/// <example>
/// <code>
/// var serializer = new BinarySerializer();
/// byte[] bytes = serializer.Serialize(new Customer { Name = "Ada", Age = 36 });
/// Customer? restored = serializer.Deserialize&lt;Customer&gt;(bytes);
/// </code>
/// </example>
/// </remarks>
public sealed class BinarySerializer
{
    private readonly BinarySerializerOptions _options;
    private readonly FormatRouter _router;

    /// <summary>Creates a serializer.</summary>
    /// <param name="options">
    /// Configuration built with <see cref="BinarySerializerOptions.Configure"/>, or
    /// <see langword="null"/> for <see cref="BinarySerializerOptions.Default"/>: format version 1,
    /// no compression, no checksum, no encryption.
    /// </param>
    /// <exception cref="Exceptions.BinaryConfigurationException">The configured limits are invalid.</exception>
    public BinarySerializer(BinarySerializerOptions? options = null)
    {
        _options = options ?? BinarySerializerOptions.Default;
        ArgumentNullException.ThrowIfNull(_options.Limits);
        _options.Limits.Validate();

        var pipelines = new Dictionary<int, IFormatPipeline>
        {
            [BinaryFormatHeaderV1.Version] = new V1FormatPipeline(
                _options.Compression,
                _options.Checksum,
                _options.Encryption,
                _options.KeyId,
                _options.Catalog)
        };

        if (_options.AllowV0Fallback)
            pipelines[0] = new V0FormatPipeline();

        _router = new FormatRouter(pipelines);
    }

    /// <summary>Writes <paramref name="data"/> to <paramref name="destination"/>.</summary>
    /// <typeparam name="T">
    /// The declared type. It decides the layout, so write and read must use the same one. A value
    /// whose runtime type differs needs a <see cref="BinaryUnionAttribute"/> declaration.
    /// </typeparam>
    /// <param name="data">The value to write. May be <see langword="null"/> for reference types.</param>
    /// <param name="destination">
    /// The stream to append to. It is left open, and its position is not reset; only the bytes this
    /// call produces count against <see cref="Security.SerializationLimits.MaxWireBytes"/>.
    /// </param>
    /// <exception cref="Exceptions.BinaryTypeException">The type or the object graph cannot be encoded.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryStreamException">The destination stream failed.</exception>
    public void Serialize<T>(Stream destination, T data) =>
        _router.ForWriting(_options.WriteVersion).Write(destination, data, BeginOperation());

    /// <summary>Writes <paramref name="data"/> to a new byte array.</summary>
    /// <typeparam name="T">The declared type; see <see cref="Serialize{T}(Stream, T)"/>.</typeparam>
    /// <param name="data">The value to write.</param>
    /// <returns>The encoded payload.</returns>
    public byte[] Serialize<T>(T data)
    {
        using var buffer = new MemoryStream();
        Serialize(buffer, data);
        return buffer.ToArray();
    }

    /// <summary>Reads a value from <paramref name="source"/>.</summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">
    /// A seekable stream positioned at the start of a payload. It is left open and is read only as far
    /// as the payload extends.
    /// </param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    /// <exception cref="NotSupportedException"><paramref name="source"/> cannot seek.</exception>
    /// <exception cref="Exceptions.BinaryFormatException">The data is malformed or truncated.</exception>
    /// <exception cref="Exceptions.BinaryLimitException">A configured limit was exceeded.</exception>
    /// <exception cref="Exceptions.BinaryIntegrityException">The checksum or authentication tag failed.</exception>
    /// <exception cref="Exceptions.BinaryEncryptionKeyException">Key material is missing or does not match.</exception>
    /// <exception cref="Exceptions.BinaryTypeException">The payload does not fit the requested type.</exception>
    public T? Deserialize<T>(Stream source) =>
        _router.ForReading(source).Read<T>(source, BeginOperation());

    /// <summary>Reads a value from a byte array.</summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="bytes">The payload. An empty array yields the default of <typeparamref name="T"/>.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    public T? Deserialize<T>(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            return default;

        using var buffer = new MemoryStream(bytes, writable: false);
        return Deserialize<T>(buffer);
    }

    /// <summary>Reads a payload into an object you already have, instead of allocating a new one.</summary>
    /// <typeparam name="T">
    /// A member-encoded type. Types with a dedicated encoding — collections, dictionaries, arrays,
    /// strings — are rejected because their payload is not a member layout.
    /// </typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">The instance to populate. Its members are overwritten.</param>
    /// <returns>The same instance, populated.</returns>
    /// <exception cref="Exceptions.BinaryTypeException">
    /// <typeparamref name="T"/> is not member-encoded, or the payload holds a different runtime type.
    /// </exception>
    public T? Deserialize<T>(Stream source, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(existingInstance);
        return _router.ForReading(source).Read(source, existingInstance, BeginOperation());
    }

    /// <summary>Reads a payload into an object you already have.</summary>
    /// <typeparam name="T">A member-encoded type; see <see cref="Deserialize{T}(Stream, T)"/>.</typeparam>
    /// <param name="bytes">The payload. An empty array leaves the instance untouched and returns <see langword="null"/>.</param>
    /// <param name="existingInstance">The instance to populate.</param>
    /// <returns>The same instance, populated.</returns>
    public T? Deserialize<T>(byte[] bytes, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(existingInstance);
        if (bytes.Length == 0)
            return null;

        using var buffer = new MemoryStream(bytes, writable: false);
        return Deserialize(buffer, existingInstance);
    }

    /// <summary>Reads a value type, assigning the result to <paramref name="existingInstance"/>.</summary>
    /// <remarks>
    /// A struct is copied by value, so this reads the root exactly as it was written and assigns it.
    /// Members absent from the payload are not preserved from the current value.
    /// </remarks>
    /// <typeparam name="T">The value type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">Receives the value read.</param>
    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct =>
        existingInstance = Deserialize<T>(source);

    /// <summary>Reads a value type from a byte array, assigning the result.</summary>
    /// <typeparam name="T">The value type the payload was written with.</typeparam>
    /// <param name="bytes">The payload. An empty array leaves <paramref name="existingInstance"/> untouched.</param>
    /// <param name="existingInstance">Receives the value read.</param>
    public void Deserialize<T>(byte[] bytes, ref T existingInstance) where T : struct
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            return;

        existingInstance = Deserialize<T>(bytes);
    }

    private SerializationOperation BeginOperation() =>
        new(_options.Limits,
            _options.Keys,
            _options.PreserveReferences,
            _options.RequireEncryption,
            _options.RequireChecksum);
}
