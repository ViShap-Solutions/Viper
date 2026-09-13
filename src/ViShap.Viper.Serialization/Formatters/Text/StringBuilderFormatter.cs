using System.Text;

namespace ViShap.Viper.Formatters;

internal sealed class StringBuilderFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(StringBuilder);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) =>
        writer.RawWriter.Write(value.ToString()!);
    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new StringBuilder(reader.RawReader.ReadString());
}