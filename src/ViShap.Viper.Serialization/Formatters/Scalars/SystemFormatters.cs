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
        if (!Version.TryParse(text, out var version) || version.ToString() != text)
            throw new BinaryFormatException(
                $"Version payload '{text}' is not a version string in its canonical form.");

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

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException ex)
        {
            throw new BinaryFormatException($"Culture name '{name}' is not a known culture.", ex);
        }

        if (culture.Name != name)
            throw new BinaryFormatException(
                $"Culture name '{name}' is not in its canonical form '{culture.Name}'.");

        return culture;
    }
}

/// <summary>
/// A bit array: its bit count, then a blob of the bytes that hold the bits. It is a reference type, so
/// the bit count is its first number and carries its null: it is written one higher.
/// </summary>
internal sealed class BitArrayFormatter : IScalarFormatter<BitArray>
{
    public int MinimumWireSize => 2;

    public void Write(ref WireWriter writer, BitArray value)
    {
        writer.WriteBitCount(value.Length, "BitArray length", nullFolded: true);

        byte[] bytes = new byte[(value.Length + 7) / 8];
        value.CopyTo(bytes, 0);
        writer.WriteBlob(bytes, "BitArray data");
    }

    public BitArray Read(ref WireReader reader)
    {
        int length = reader.ReadBitCount("BitArray length", nullFolded: true);
        byte[] bytes = reader.ReadBlob("BitArray data");

        int expected = (length + 7) / 8;
        if (bytes.Length != expected)
            throw new BinaryFormatException(
                $"BitArray declares {length} bit(s), which needs {expected} byte(s), but {bytes.Length} " +
                "were present.");

        int usedBits = length % 8;
        if (usedBits != 0 && bytes[^1] >> usedBits != 0)
            throw new BinaryFormatException(
                $"BitArray declares {length} bit(s), but a bit past the last one is set.");

        return new BitArray(bytes) { Length = length };
    }
}
