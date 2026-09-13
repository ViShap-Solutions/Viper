using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class Vector3Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Vector3);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var v = (Vector3)value;
        writer.RawWriter.Write(v.X);
        writer.RawWriter.Write(v.Y);
        writer.RawWriter.Write(v.Z);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Vector3(reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}