using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class Matrix4x4Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Matrix4x4);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var m = (Matrix4x4)value;
        writer.RawWriter.Write(m.M11); writer.RawWriter.Write(m.M12); writer.RawWriter.Write(m.M13); writer.RawWriter.Write(m.M14);
        writer.RawWriter.Write(m.M21); writer.RawWriter.Write(m.M22); writer.RawWriter.Write(m.M23); writer.RawWriter.Write(m.M24);
        writer.RawWriter.Write(m.M31); writer.RawWriter.Write(m.M32); writer.RawWriter.Write(m.M33); writer.RawWriter.Write(m.M34);
        writer.RawWriter.Write(m.M41); writer.RawWriter.Write(m.M42); writer.RawWriter.Write(m.M43); writer.RawWriter.Write(m.M44);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Matrix4x4(
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}