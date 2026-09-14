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
        int length = DeserializationGuard.ValidateBitCount(
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxByteBlobLength,
            "BitArray length");

        int byteLength = (int)(((long)length + 7L) / 8L);

        byte[] bytes = DeserializationGuard.ReadValidatedBytes(
            reader,
            byteLength,
            reader.Budget.Limits.MaxByteBlobLength,
            "BitArray data length");

        return new BitArray(bytes) { Length = length };
    }
}