using System.Numerics;

namespace ViShap.Viper.Formatters;

internal sealed class ComplexFormatter : IScalarFormatter<Complex>
{
    public int MinimumWireSize => 2 * sizeof(double);

    public void Write(ref WireWriter writer, Complex value)
    {
        writer.WriteDouble(value.Real);
        writer.WriteDouble(value.Imaginary);
    }

    public Complex Read(ref WireReader reader) => new(reader.ReadDouble(), reader.ReadDouble());
}

internal sealed class Vector2Formatter : IScalarFormatter<Vector2>
{
    public int MinimumWireSize => 2 * sizeof(float);

    public void Write(ref WireWriter writer, Vector2 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
    }

    public Vector2 Read(ref WireReader reader) => new(reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Vector3Formatter : IScalarFormatter<Vector3>
{
    public int MinimumWireSize => 3 * sizeof(float);

    public void Write(ref WireWriter writer, Vector3 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
    }

    public Vector3 Read(ref WireReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Vector4Formatter : IScalarFormatter<Vector4>
{
    public int MinimumWireSize => 4 * sizeof(float);

    public void Write(ref WireWriter writer, Vector4 value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
        writer.WriteSingle(value.W);
    }

    public Vector4 Read(ref WireReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class QuaternionFormatter : IScalarFormatter<Quaternion>
{
    public int MinimumWireSize => 4 * sizeof(float);

    public void Write(ref WireWriter writer, Quaternion value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
        writer.WriteSingle(value.W);
    }

    public Quaternion Read(ref WireReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class PlaneFormatter : IScalarFormatter<Plane>
{
    public int MinimumWireSize => 4 * sizeof(float);

    public void Write(ref WireWriter writer, Plane value)
    {
        writer.WriteSingle(value.Normal.X);
        writer.WriteSingle(value.Normal.Y);
        writer.WriteSingle(value.Normal.Z);
        writer.WriteSingle(value.D);
    }

    public Plane Read(ref WireReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Matrix3x2Formatter : IScalarFormatter<Matrix3x2>
{
    public int MinimumWireSize => 6 * sizeof(float);

    public void Write(ref WireWriter writer, Matrix3x2 value)
    {
        writer.WriteSingle(value.M11);
        writer.WriteSingle(value.M12);
        writer.WriteSingle(value.M21);
        writer.WriteSingle(value.M22);
        writer.WriteSingle(value.M31);
        writer.WriteSingle(value.M32);
    }

    public Matrix3x2 Read(ref WireReader reader) =>
        new(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());
}

internal sealed class Matrix4x4Formatter : IScalarFormatter<Matrix4x4>
{
    public int MinimumWireSize => 16 * sizeof(float);

    public void Write(ref WireWriter writer, Matrix4x4 value)
    {
        writer.WriteSingle(value.M11); writer.WriteSingle(value.M12);
        writer.WriteSingle(value.M13); writer.WriteSingle(value.M14);
        writer.WriteSingle(value.M21); writer.WriteSingle(value.M22);
        writer.WriteSingle(value.M23); writer.WriteSingle(value.M24);
        writer.WriteSingle(value.M31); writer.WriteSingle(value.M32);
        writer.WriteSingle(value.M33); writer.WriteSingle(value.M34);
        writer.WriteSingle(value.M41); writer.WriteSingle(value.M42);
        writer.WriteSingle(value.M43); writer.WriteSingle(value.M44);
    }

    public Matrix4x4 Read(ref WireReader reader) =>
        new(
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
