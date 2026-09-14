using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class V0FormatCodec : IFormatCodec
{
    private readonly DeserializationLimits _limits;
    public int Version => 0;
    
    public V0FormatCodec(DeserializationLimits limits)
    {
        _limits = limits;
        _limits.Validate();
    }

    public void Serialize<T>(Stream destination, T data)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadWriter(writer, preserveReferences: false, limits: _limits).Serialize(data);
        writer.Flush();
    }

    public T? Deserialize<T>(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        return new BinaryPayloadReader(reader, preserveReferences: false, limits: _limits).Deserialize<T>();
    }

    public T? Deserialize<T>(Stream source, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        return new BinaryPayloadReader(reader, preserveReferences: false, limits: _limits).Deserialize(existingInstance);
    }
    
    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadReader(reader, preserveReferences: false, limits: _limits).Deserialize(ref existingInstance);
    }
}