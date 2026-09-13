namespace ViShap.Viper.Formatters;

internal sealed class HalfFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Half);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.RawWriter.Write(BitConverter.HalfToInt16Bits((Half)value));
    public object Read(BinaryPayloadReader reader, Type declaredType) => BitConverter.Int16BitsToHalf(reader.RawReader.ReadInt16());
}