using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class Vector4Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Vector4);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var v = (Vector4)value;
        writer.RawWriter.Write(v.X);
        writer.RawWriter.Write(v.Y);
        writer.RawWriter.Write(v.Z);
        writer.RawWriter.Write(v.W);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Vector4(
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}