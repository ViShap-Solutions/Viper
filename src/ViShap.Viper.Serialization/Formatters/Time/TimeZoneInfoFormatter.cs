namespace ViShap.Viper.Formatters;

internal sealed class TimeZoneInfoFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(TimeZoneInfo);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(((TimeZoneInfo)value).ToSerializedString());
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        TimeZoneInfo.FromSerializedString(
            DeserializationGuard.ReadString(
                reader,
                reader.Budget.Limits.MaxStringLength,
                "TimeZoneInfo serialized string length"));
}