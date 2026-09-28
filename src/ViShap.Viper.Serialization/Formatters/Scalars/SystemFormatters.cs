using System.Collections;
using System.Globalization;
using System.Text;

namespace ViShap.Viper.Formatters;

internal sealed class GuidFormatter : IScalarFormatter<Guid>
{
    private const int Size = 16;

    public int MinimumWireSize => Size;

    public void Write(ref WireWriter writer, Guid value)
    {
        Span<byte> buffer = stackalloc byte[Size];
        value.TryWriteBytes(buffer);
        writer.Write(buffer);
    }

    public Guid Read(ref WireReader reader)
    {
        Span<byte> buffer = stackalloc byte[Size];
        reader.ReadExact(buffer, "Guid");
        return new Guid(buffer);
    }
}

internal sealed class UriFormatter : IScalarFormatter<Uri>
{
    public int MinimumWireSize => 1;

    public void Write(ref WireWriter writer, Uri value) => writer.WriteString(value.OriginalString);

    public Uri Read(ref WireReader reader)
    {
        string text = reader.ReadString();
        if (!Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var uri))
            throw new BinaryFormatException("URI payload is not a valid URI.");

        return uri;
    }
}

internal sealed class VersionFormatter : IScalarFormatter<Version>
{
    public int MinimumWireSize => 1;

    public void Write(ref WireWriter writer, Version value) => writer.WriteString(value.ToString());

    public Version Read(ref WireReader reader)
    {
        string text = reader.ReadString();
        if (!Version.TryParse(text, out var version))
            throw new BinaryFormatException("Version payload is not a valid version string.");

        return version;
    }
}

internal sealed class StringBuilderFormatter : IScalarFormatter<StringBuilder>
{
    public int MinimumWireSize => 1;

    public void Write(ref WireWriter writer, StringBuilder value) => writer.WriteString(value.ToString());

    public StringBuilder Read(ref WireReader reader) => new(reader.ReadString());
}

internal sealed class CultureInfoFormatter : IScalarFormatter<CultureInfo>
{
    public int MinimumWireSize => 1;

    public void Write(ref WireWriter writer, CultureInfo value) => writer.WriteString(value.Name);

    public CultureInfo Read(ref WireReader reader)
    {
        string name = reader.ReadString();

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException ex)
        {
            throw new BinaryFormatException($"Culture name '{name}' is not a known culture.", ex);
        }
    }
}

internal sealed class BitArrayFormatter : IScalarFormatter<BitArray>
{
    public int MinimumWireSize => sizeof(int) + 1;

    public void Write(ref WireWriter writer, BitArray value)
    {
        writer.WriteBitCount(value.Length, "BitArray length");

        byte[] bytes = new byte[(value.Length + 7) / 8];
        value.CopyTo(bytes, 0);
        writer.WriteBlob(bytes, "BitArray data");
    }

    public BitArray Read(ref WireReader reader)
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
