namespace ViShap.Viper;

/// <summary>
/// Convenience methods that serialize to and from a <see cref="Stream"/> without constructing a
/// <see cref="BinarySerializer"/> first.
/// </summary>
/// <remarks>
/// <para>
/// Each call builds a serializer, so these are meant for occasional use and for cases where the
/// payload itself decides the configuration. In a hot path, create one <see cref="BinarySerializer"/>
/// and reuse it: it caches per-type metadata that a throwaway instance rebuilds.
/// </para>
/// <para>
/// The overloads that take a key, a key resolver, or nothing at all first inspect the payload's
/// header and configure themselves from it — useful when you read data written elsewhere and do not
/// know up front whether it is compressed or encrypted.
/// </para>
/// </remarks>
public static class StreamExtensions
{
    // --- Serialization ---

    /// <summary>Writes <paramref name="data"/> to the stream.</summary>
    /// <typeparam name="T">The declared type, which decides the layout.</typeparam>
    /// <param name="destination">The stream to append to. It is left open.</param>
    /// <param name="data">The value to write.</param>
    /// <param name="options">Configuration, or <see langword="null"/> for the defaults.</param>
    public static void Serialize<T>(this Stream destination, T data, BinarySerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var serializer = new BinarySerializer(options ?? BinarySerializerOptions.Default);
        serializer.Serialize(destination, data);
    }

    // --- Deserialization into a new instance ---

    /// <summary>Reads a value using an explicit configuration.</summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="options">Configuration to read with.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    public static T? Deserialize<T>(this Stream source, BinarySerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        return serializer.Deserialize<T>(source);
    }

    /// <summary>
    /// Reads a value, configuring the reader from the payload's own header.
    /// </summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    public static T? Deserialize<T>(this Stream source) =>
        source.Deserialize<T>((byte[]?)null);

    /// <summary>Reads a value, configuring the reader from the header and decrypting with <paramref name="key"/>.</summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="key">Key material, copied immediately; <see langword="null"/> when the payload is not encrypted.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    public static T? Deserialize<T>(this Stream source, byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = BinarySerializerOptions.FromStream(source, key);
        return source.Deserialize<T>(options);
    }

    /// <summary>
    /// Reads a value, configuring the reader from the header and resolving the key by the id the
    /// payload names.
    /// </summary>
    /// <typeparam name="T">The declared type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="keyResolver">Returns the key for a given id.</param>
    /// <returns>The value, or <see langword="null"/> if the payload holds a null root.</returns>
    public static T? Deserialize<T>(this Stream source, Func<string?, byte[]?> keyResolver)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        return source.Deserialize<T>(options);
    }

    // --- Deserialization into an existing reference type ---

    /// <summary>Reads a payload into an object you already have, using an explicit configuration.</summary>
    /// <typeparam name="T">A member-encoded type; types with a dedicated encoding are rejected.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">The instance to populate.</param>
    /// <param name="options">Configuration to read with.</param>
    /// <returns>The same instance, populated.</returns>
    public static T? Deserialize<T>(this Stream source, T existingInstance, BinarySerializerOptions options) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        return serializer.Deserialize(source, existingInstance);
    }

    /// <summary>Reads a payload into an object you already have, configuring the reader from the header.</summary>
    /// <typeparam name="T">A member-encoded type.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">The instance to populate.</param>
    /// <returns>The same instance, populated.</returns>
    public static T? Deserialize<T>(this Stream source, T existingInstance) where T : class =>
        source.Deserialize(existingInstance, (byte[]?)null);

    /// <summary>Reads a payload into an object you already have, decrypting with <paramref name="key"/>.</summary>
    /// <typeparam name="T">A member-encoded type.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">The instance to populate.</param>
    /// <param name="key">Key material, copied immediately; <see langword="null"/> when the payload is not encrypted.</param>
    /// <returns>The same instance, populated.</returns>
    public static T? Deserialize<T>(this Stream source, T existingInstance, byte[]? key) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);

        var options = BinarySerializerOptions.FromStream(source, key);
        return source.Deserialize(existingInstance, options);
    }

    /// <summary>Reads a payload into an object you already have, resolving the key by id.</summary>
    /// <typeparam name="T">A member-encoded type.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">The instance to populate.</param>
    /// <param name="keyResolver">Returns the key for a given id.</param>
    /// <returns>The same instance, populated.</returns>
    public static T? Deserialize<T>(this Stream source, T existingInstance, Func<string?, byte[]?> keyResolver) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        return source.Deserialize(existingInstance, options);
    }

    // --- Deserialization into an existing value type ---

    /// <summary>Reads a value type, assigning the result, using an explicit configuration.</summary>
    /// <typeparam name="T">The value type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">Receives the value read.</param>
    /// <param name="options">Configuration to read with.</param>
    public static void Deserialize<T>(this Stream source, ref T existingInstance, BinarySerializerOptions options) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        serializer.Deserialize(source, ref existingInstance);
    }

    /// <summary>Reads a value type, assigning the result, configuring the reader from the header.</summary>
    /// <typeparam name="T">The value type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">Receives the value read.</param>
    public static void Deserialize<T>(this Stream source, ref T existingInstance) where T : struct =>
        source.Deserialize(ref existingInstance, (byte[]?)null);

    /// <summary>Reads a value type, assigning the result, decrypting with <paramref name="key"/>.</summary>
    /// <typeparam name="T">The value type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">Receives the value read.</param>
    /// <param name="key">Key material, copied immediately; <see langword="null"/> when the payload is not encrypted.</param>
    public static void Deserialize<T>(this Stream source, ref T existingInstance, byte[]? key) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = BinarySerializerOptions.FromStream(source, key);
        source.Deserialize(ref existingInstance, options);
    }

    /// <summary>Reads a value type, assigning the result, resolving the key by id.</summary>
    /// <typeparam name="T">The value type the payload was written with.</typeparam>
    /// <param name="source">A seekable stream positioned at the start of a payload.</param>
    /// <param name="existingInstance">Receives the value read.</param>
    /// <param name="keyResolver">Returns the key for a given id.</param>
    public static void Deserialize<T>(this Stream source, ref T existingInstance, Func<string?, byte[]?> keyResolver) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        source.Deserialize(ref existingInstance, options);
    }
}
