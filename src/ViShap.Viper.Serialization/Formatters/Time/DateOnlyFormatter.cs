namespace ViShap.Viper.Formatters;

internal sealed class DateOnlyFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(DateOnly);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.RawWriter.Write(((DateOnly)value).DayNumber);
    public object Read(BinaryPayloadReader reader, Type declaredType) => DateOnly.FromDayNumber(reader.RawReader.ReadInt32());
}