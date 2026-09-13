namespace ViShap.Viper;

public static class StreamExtensions
{
    // --- Standard Serialization ---
    public static void Serialize<T>(this Stream destination, T data, BinarySerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var serializer = new BinarySerializer(options ?? BinarySerializerOptions.Default);
        serializer.Serialize(destination, data);
    }
    
    // --- New Instance Deserialization ---
    public static T? Deserialize<T>(this Stream source, BinarySerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        return serializer.Deserialize<T>(source);
    }

    public static T? Deserialize<T>(this Stream source) =>
        source.Deserialize<T>((byte[]?)null);

    public static T? Deserialize<T>(this Stream source, byte[]? key)
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = BinarySerializerOptions.FromStream(source, key);
        return source.Deserialize<T>(options);
    }

    public static T? Deserialize<T>(this Stream source, Func<string?, byte[]?> keyResolver)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        return source.Deserialize<T>(options);
    }

    // --- Reference Type Existing Instance Deserialization ---
    public static T? Deserialize<T>(this Stream source, T existingInstance, BinarySerializerOptions options) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        return serializer.Deserialize(source, existingInstance);
    }

    public static T? Deserialize<T>(this Stream source, T existingInstance) where T : class =>
        source.Deserialize(existingInstance, (byte[]?)null);

    public static T? Deserialize<T>(this Stream source, T existingInstance, byte[]? key) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);

        var options = BinarySerializerOptions.FromStream(source, key);
        return source.Deserialize(existingInstance, options);
    }

    public static T? Deserialize<T>(this Stream source, T existingInstance, Func<string?, byte[]?> keyResolver) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        return source.Deserialize(existingInstance, options);
    }
    
    // --- Value Type Existing Instance Deserialization ---
    public static void Deserialize<T>(this Stream source, ref T existingInstance, BinarySerializerOptions options) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var serializer = new BinarySerializer(options);
        serializer.Deserialize(source, ref existingInstance);
    }

    public static void Deserialize<T>(this Stream source, ref T existingInstance) where T : struct =>
        source.Deserialize(ref existingInstance, (byte[]?)null);

    public static void Deserialize<T>(this Stream source, ref T existingInstance, byte[]? key) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = BinarySerializerOptions.FromStream(source, key);
        source.Deserialize(ref existingInstance, options);
    }

    public static void Deserialize<T>(this Stream source, ref T existingInstance, Func<string?, byte[]?> keyResolver) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var options = BinarySerializerOptions.FromStream(source, keyResolver);
        source.Deserialize(ref existingInstance, options);
    }
}