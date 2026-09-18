using System.Collections;

namespace ViShap.Viper.Formatters;

internal sealed class BitArrayFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(BitArray);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var bits = (BitArray)value;
        int length = writer.ValidateBitCountForWrite(bits.Length, "BitArray length");
        int byteLength = checked((length + 7) / 8);
        writer.ValidateByteBlobLengthForWrite(byteLength, "BitArray data length");
        writer.WriteInt32(length);
        var bytes = new byte[byteLength];
        bits.CopyTo(bytes, 0);
        writer.RawWriter.Write(bytes);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        int length = DeserializationGuard.ValidateBitCount(
            reader.RawReader.ReadInt32(),
            reader.Budget.Limits.MaxByteBlobBytes,
            "BitArray length");

        int byteLength = (int)(((long)length + 7L) / 8L);

        byte[] bytes = DeserializationGuard.ReadValidatedBytes(
            reader,
            byteLength,
            reader.Budget.Limits.MaxByteBlobBytes,
            "BitArray data length");

        return new BitArray(bytes) { Length = length };
    }
}