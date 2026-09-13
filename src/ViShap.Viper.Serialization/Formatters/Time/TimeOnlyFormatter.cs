namespace ViShap.Viper.Formatters;

internal sealed class TimeOnlyFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(TimeOnly);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.RawWriter.Write(((TimeOnly)value).Ticks);
    public object Read(BinaryPayloadReader reader, Type declaredType) => new TimeOnly(reader.RawReader.ReadInt64());
}