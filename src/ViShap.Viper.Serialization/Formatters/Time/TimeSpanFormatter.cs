namespace ViShap.Viper.Formatters;

internal sealed class TimeSpanFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(TimeSpan);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.RawWriter.Write(((TimeSpan)value).Ticks);
    public object Read(BinaryPayloadReader reader, Type declaredType) => TimeSpan.FromTicks(reader.RawReader.ReadInt64());
}