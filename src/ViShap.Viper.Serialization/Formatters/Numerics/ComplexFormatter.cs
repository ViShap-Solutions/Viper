using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class ComplexFormatter : ITypeFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Complex);

    public void Write(BinaryPayloadWriter writer, object value, Type declaredType)
    {
        var complex = (Complex)value;
        writer.RawWriter.Write(complex.Real);
        writer.RawWriter.Write(complex.Imaginary);
    }

    public object Read(BinaryPayloadReader reader, Type declaredType) =>
        new Complex(reader.RawReader.ReadDouble(), reader.RawReader.ReadDouble());
}