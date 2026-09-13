namespace ViShap.Viper.Formatters;

internal sealed class DateTimeFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(DateTime);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.RawWriter.Write(((DateTime)value).ToBinary());
    public object Read(BinaryPayloadReader reader, Type declaredType) => DateTime.FromBinary(reader.RawReader.ReadInt64());
}