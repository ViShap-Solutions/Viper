using System.Buffers.Binary;

namespace ViShap.Viper.Format;

internal static class BinaryHeaderPeek
{
    private const int PrefixLength = 8;

    public static bool TryPeekMagicAndVersion(Stream source, out int formatVersion)
    {
        long position = source.Position;
        Span<byte> buffer = stackalloc byte[PrefixLength];
        int read = 0;
        try
        {
            while (read < buffer.Length)
            {
                int current = source.Read(buffer[read..]);
                if (current <= 0)
                    break;

                read += current;
            }
        }
        finally
        {
            source.Position = position;
        }

        return TryReadMagicAndVersion(buffer[..read], out formatVersion);
    }

    public static bool TryReadMagicAndVersion(ReadOnlySpan<byte> source, out int formatVersion)
    {
        if (source.Length >= PrefixLength
            && BinaryPrimitives.ReadInt32LittleEndian(source) == BinaryFormatConstants.Magic)
        {
            formatVersion = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
            return true;
        }

        formatVersion = 0;
        return false;
    }
}
