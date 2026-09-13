using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class Matrix3x2Formatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Matrix3x2);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var m = (Matrix3x2)value;
        writer.RawWriter.Write(m.M11);
        writer.RawWriter.Write(m.M12);
        writer.RawWriter.Write(m.M21);
        writer.RawWriter.Write(m.M22);
        writer.RawWriter.Write(m.M31);
        writer.RawWriter.Write(m.M32);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Matrix3x2(
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}