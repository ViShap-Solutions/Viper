using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class QuaternionFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Quaternion);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var q = (Quaternion)value;
        writer.RawWriter.Write(q.X);
        writer.RawWriter.Write(q.Y);
        writer.RawWriter.Write(q.Z);
        writer.RawWriter.Write(q.W);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Quaternion(
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}