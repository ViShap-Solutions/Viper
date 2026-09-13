namespace ViShap.Viper.Formatters;

internal sealed class DateTimeOffsetFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(DateTimeOffset);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var dto = (DateTimeOffset)value;
        writer.RawWriter.Write(dto.Ticks);
        writer.RawWriter.Write(dto.Offset.Ticks);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType)
    {
        long ticks = reader.RawReader.ReadInt64();
        long offsetTicks = reader.RawReader.ReadInt64();
        return new DateTimeOffset(ticks, new TimeSpan(offsetTicks));
    }
}