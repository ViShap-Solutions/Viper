using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class PlaneFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Plane);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var p = (Plane)value;
        writer.RawWriter.Write(p.Normal.X);
        writer.RawWriter.Write(p.Normal.Y);
        writer.RawWriter.Write(p.Normal.Z);
        writer.RawWriter.Write(p.D);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Plane(
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle(),
            reader.RawReader.ReadSingle(), reader.RawReader.ReadSingle());
}