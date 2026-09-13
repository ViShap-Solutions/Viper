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

    public void Serialize<T>(Stream destination, T data) where T : class =>
        _router.Serialize(destination, data, _options.WriteVersion);
    
    public byte[] Serialize<T>(T data) where T : class
    {
        using var ms = new MemoryStream();
        Serialize(ms, data);
        return ms.ToArray();
    }

    public T? Deserialize<T>(Stream source) where T : class => 
        _router.Deserialize<T>(source);
    
    public T? Deserialize<T>(byte[] bytes) where T : class
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0) return null;

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
}