using System.Text;

namespace ViShap.Viper.Formatters;

internal sealed class RuneFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Rune);
    public void Write(BinaryPayloadWriter writer, object value, Type declaredType) => writer.RawWriter.Write(((Rune)value).Value);
    public object Read(BinaryPayloadReader reader, Type declaredType) => new Rune(reader.RawReader.ReadInt32());
}