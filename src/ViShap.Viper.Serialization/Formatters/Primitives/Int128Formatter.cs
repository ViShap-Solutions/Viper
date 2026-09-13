using System.Buffers.Binary;

namespace ViShap.Viper.Formatters;

internal sealed class Int128Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Int128);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteInt128LittleEndian(buffer, (Int128)value);
        writer.RawWriter.Write(buffer);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        BinaryPrimitives.ReadInt128LittleEndian(reader.RawReader.ReadBytes(16));
}