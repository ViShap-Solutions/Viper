using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class ComplexFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Complex);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var complex = (Complex)value;
        writer.WriteDouble(complex.Real);
        writer.WriteDouble(complex.Imaginary);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Complex(reader.ReadDouble(), reader.ReadDouble());
}

internal sealed class Vector2Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Vector2);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var vector = (Vector2)value;
        writer.WriteSingle(vector.X);
        writer.WriteSingle(vector.Y);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Vector2(reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Vector3Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Vector3);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var vector = (Vector3)value;
        writer.WriteSingle(vector.X);
        writer.WriteSingle(vector.Y);
        writer.WriteSingle(vector.Z);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Vector4Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Vector4);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var vector = (Vector4)value;
        writer.WriteSingle(vector.X);
        writer.WriteSingle(vector.Y);
        writer.WriteSingle(vector.Z);
        writer.WriteSingle(vector.W);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Vector4(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class QuaternionFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Quaternion);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var quaternion = (Quaternion)value;
        writer.WriteSingle(quaternion.X);
        writer.WriteSingle(quaternion.Y);
        writer.WriteSingle(quaternion.Z);
        writer.WriteSingle(quaternion.W);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Quaternion(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class PlaneFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Plane);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var plane = (Plane)value;
        writer.WriteSingle(plane.Normal.X);
        writer.WriteSingle(plane.Normal.Y);
        writer.WriteSingle(plane.Normal.Z);
        writer.WriteSingle(plane.D);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Plane(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Matrix3x2Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Matrix3x2);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var matrix = (Matrix3x2)value;
        writer.WriteSingle(matrix.M11);
        writer.WriteSingle(matrix.M12);
        writer.WriteSingle(matrix.M21);
        writer.WriteSingle(matrix.M22);
        writer.WriteSingle(matrix.M31);
        writer.WriteSingle(matrix.M32);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Matrix3x2(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Matrix4x4Formatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Matrix4x4);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var matrix = (Matrix4x4)value;
        writer.WriteSingle(matrix.M11); writer.WriteSingle(matrix.M12);
        writer.WriteSingle(matrix.M13); writer.WriteSingle(matrix.M14);
        writer.WriteSingle(matrix.M21); writer.WriteSingle(matrix.M22);
        writer.WriteSingle(matrix.M23); writer.WriteSingle(matrix.M24);
        writer.WriteSingle(matrix.M31); writer.WriteSingle(matrix.M32);
        writer.WriteSingle(matrix.M33); writer.WriteSingle(matrix.M34);
        writer.WriteSingle(matrix.M41); writer.WriteSingle(matrix.M42);
        writer.WriteSingle(matrix.M43); writer.WriteSingle(matrix.M44);
    }

    public object Read(ValueReader reader, Type declaredType) =>
        new Matrix4x4(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
