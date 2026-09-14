using System.Globalization;

namespace ViShap.Viper.Formatters;

internal sealed class CultureInfoFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(CultureInfo);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(((CultureInfo)value).Name);
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        CultureInfo.GetCultureInfo(
            DeserializationGuard.ReadString(
                reader,
                reader.Budget.Limits.MaxStringLength,
                "Culture name length"));
}