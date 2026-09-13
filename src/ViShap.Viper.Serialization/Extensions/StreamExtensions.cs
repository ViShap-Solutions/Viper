namespace ViShap.Viper;

public static class StreamExtensions
{
    // --- Standard Serialization ---
    public static void Serialize<T>(this Stream destination, T data, BinarySerializerOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(destination);

        var serializer = new BinarySerializer(options ?? BinarySerializerOptions.Default);
        serializer.Serialize(destination, data);
    }
    
    // --- New Instance Deserialization ---
    public static T? Deserialize<T>(this Stream source, BinarySerializerOptions options) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        return serializer.Deserialize<T>(source);
    }

    public static T? Deserialize<T>(this Stream source) where T : class =>
        Deserialize<T>(source, (byte[]?)null);

    public static T? Deserialize<T>(this Stream source, byte[]? key) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = BinarySerializerOptions.FromStream(source, key);
        return Deserialize<T>(source, options);
    }

    public static T? Deserialize<T>(this Stream source, Func<string?, byte[]?> keyResolver) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        return Deserialize<T>(source, options);
    }

    // --- Existing Instance Deserialization ---
    public static T? Deserialize<T>(this Stream source, T existingInstance, BinarySerializerOptions options) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        return serializer.Deserialize(source, existingInstance);
    }

    public static T? Deserialize<T>(this Stream source, T existingInstance) where T : class =>
        Deserialize(source, existingInstance, (byte[]?)null);

    public static T? Deserialize<T>(this Stream source, T existingInstance, byte[]? key) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);

        var options = BinarySerializerOptions.FromStream(source, key);
        return Deserialize(source, existingInstance, options);
    }

    public static T? Deserialize<T>(this Stream source, T existingInstance, Func<string?, byte[]?> keyResolver) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        return Deserialize(source, existingInstance, options);
    }
}