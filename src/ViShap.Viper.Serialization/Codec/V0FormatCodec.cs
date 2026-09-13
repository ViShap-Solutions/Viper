using System.Text;

namespace ViShap.Viper.Codec;

internal sealed class V0FormatCodec : IFormatCodec
{
    public int Version => 0;

    public void Serialize<T>(Stream destination, T data)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadWriter(writer).Serialize(data);
        writer.Flush();
    }

    public T? Deserialize<T>(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        return new BinaryPayloadReader(reader).Deserialize<T>();
    }

    public T? Deserialize<T>(Stream source, T existingInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(existingInstance);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        return new BinaryPayloadReader(reader, preserveReferences: false).Deserialize(existingInstance);
    }
    
    public void Deserialize<T>(Stream source, ref T existingInstance) where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        using var reader = new BinaryReader(source, Encoding.UTF8, leaveOpen: true);
        new BinaryPayloadReader(reader).Deserialize(ref existingInstance);
    }
}