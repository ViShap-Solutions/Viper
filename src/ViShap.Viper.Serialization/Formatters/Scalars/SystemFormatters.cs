using System.Collections;
using System.Globalization;
using System.Text;

namespace ViShap.Viper.Formatters;

internal sealed class GuidFormatter : IScalarFormatter
{
    private const int Size = 16;

    public bool CanHandle(Type declaredType) => declaredType == typeof(Guid);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[Size];
        ((Guid)value).TryWriteBytes(buffer);
        writer.Write(buffer);
    }

    public object Read(ValueReader reader, Type declaredType)
    {
        Span<byte> buffer = stackalloc byte[Size];
        reader.ReadExact(buffer, "Guid");
        return new Guid(buffer);
    }
}

internal sealed class UriFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Uri);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteString(((Uri)value).OriginalString);

    public object Read(ValueReader reader, Type declaredType)
    {
        string text = reader.ReadString();
        if (!Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var uri))
            throw new BinaryFormatException("URI payload is not a valid URI.");

        return uri;
    }
}

internal sealed class VersionFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(Version);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteString(((Version)value).ToString());

    public object Read(ValueReader reader, Type declaredType)
    {
        string text = reader.ReadString();
        if (!Version.TryParse(text, out var version))
            throw new BinaryFormatException("Version payload is not a valid version string.");

        return version;
    }
}

internal sealed class StringBuilderFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(StringBuilder);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteString(value.ToString()!);

    public object Read(ValueReader reader, Type declaredType) =>
        new StringBuilder(reader.ReadString());
}

internal sealed class CultureInfoFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(CultureInfo);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        writer.WriteString(((CultureInfo)value).Name);

    public object Read(ValueReader reader, Type declaredType)
    {
        string name = reader.ReadString();

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException ex)
        {
            throw new BinaryFormatException(
                $"Culture name '{name}' is not a known culture.", ex);
        }
    }
}

internal sealed class BitArrayFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => declaredType == typeof(BitArray);

    public void Write(ValueWriter writer, object value, Type declaredType)
    {
        var bits = (BitArray)value;
        writer.WriteBitCount(bits.Length, "BitArray length");

        byte[] bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        writer.WriteBlob(bytes, "BitArray data");
    }

    public object Read(ValueReader reader, Type declaredType)
    {
        int length = reader.ReadBitCount("BitArray length");
        byte[] bytes = reader.ReadBlob("BitArray data");

        int expected = (length + 7) / 8;
        if (bytes.Length != expected)
            throw new BinaryFormatException(
                $"BitArray declares {length} bit(s), which needs {expected} byte(s), but {bytes.Length} " +
                "were present.");

        return new BitArray(bytes) { Length = length };
    }
}

internal sealed class DelegateFormatter : IScalarFormatter
{
    public bool CanHandle(Type declaredType) => typeof(Delegate).IsAssignableFrom(declaredType);

    public void Write(ValueWriter writer, object value, Type declaredType) =>
        throw new BinaryTypeException(
            $"Delegate types cannot be serialized ('{declaredType}') — as a root value, a member, " +
            "or a collection element. Exclude the containing member with [BinaryIgnore] instead.");

    public object Read(ValueReader reader, Type declaredType) =>
        throw new BinaryTypeException($"Delegate types cannot be deserialized ('{declaredType}').");
}
