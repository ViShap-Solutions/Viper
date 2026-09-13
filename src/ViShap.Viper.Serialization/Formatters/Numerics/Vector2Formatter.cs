using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class Vector2Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Vector2);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var v = (Vector2)value;
        writer.RawWriter.Write(v.X);
        writer.RawWriter.Write(v.Y);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Vector2(reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}