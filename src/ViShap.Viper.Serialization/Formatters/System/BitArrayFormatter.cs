using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class BitArrayFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(BitArray);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var bits = (BitArray)value;
        writer.WriteInt32(bits.Length);
        var bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        writer.RawWriter.Write(bytes);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        int length = reader.ReadInt32();
        var bytes = reader.RawReader.ReadBytes((length + 7) / 8);
        return new BitArray(bytes) { Length = length };
    }
}