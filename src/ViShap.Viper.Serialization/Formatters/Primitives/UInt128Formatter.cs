using System.Buffers.Binary;

namespace ViShap.Viper.Formatters;

internal sealed class UInt128Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(UInt128);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128LittleEndian(buffer, (UInt128)value);
        writer.RawWriter.Write(buffer);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        BinaryPrimitives.ReadUInt128LittleEndian(reader.RawReader.ReadBytes(16));
}