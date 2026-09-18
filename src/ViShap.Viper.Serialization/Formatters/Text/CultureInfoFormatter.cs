using System.Globalization;

namespace ViShap.Viper.Formatters;

internal sealed class CultureInfoFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(CultureInfo);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.WriteString(((CultureInfo)value).Name);
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        CultureInfo.GetCultureInfo(
            DeserializationGuard.ReadString(
                reader,
                reader.Budget.Limits.MaxStringBytes,
                "Culture name length"));
}