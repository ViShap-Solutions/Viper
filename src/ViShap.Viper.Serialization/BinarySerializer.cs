namespace ViShap.Viper;

public sealed class BinarySerializer
{
    private readonly BinaryFormatRouter _router;
    private readonly BinarySerializerOptions _options;

    public BinarySerializer(BinarySerializerOptions? options = null)
    {
        _options = options ?? BinarySerializerOptions.Default;
        var codecs = CodecRegistry.CreateCodecs(_options);
        _router = new BinaryFormatRouter(codecs);
    }

    public void Serialize<T>(Stream destination, T data) =>
        _router.Serialize(destination, data, _options.WriteVersion);
    
    public byte[] Serialize<T>(T data)
    {
        using var ms = new MemoryStream();
        Serialize(ms, data);
        return ms.ToArray();
    }

    public T? Deserialize<T>(Stream source) =>
        _router.Deserialize<T>(source);
    
    public T? Deserialize<T>(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return default;

        using var ms = new MemoryStream(bytes);
        return Deserialize<T>(ms);
    }
    
    public T? Deserialize<T>(Stream source, T existingInstance) where T : class =>
        _router.Deserialize(source, existingInstance);
    
    public T? Deserialize<T>(byte[] bytes, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return null;
        ArgumentNullException.ThrowIfNull(existingInstance);
        
        using var ms = new MemoryStream(bytes);
        return _router.Deserialize(ms, existingInstance);
    }
    
    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct =>
        _router.Deserialize(source, ref existingInstance);
    
    public void Deserialize<T>(byte[] bytes, ref T existingInstance) where T : struct
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return;
        using var ms = new MemoryStream(bytes);
        _router.Deserialize(ms, ref existingInstance);
    }
}