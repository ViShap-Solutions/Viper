namespace ViShap.Viper.Format;

internal static class BinaryHeaderPeek
{
    public static bool TryPeekMagicAndVersion(Stream source, out int formatVersion)
    {
        long position = source.Position;
        Span<byte> buffer = stackalloc byte[8];
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

        if (read == 8 && BitConverter.ToInt32(buffer) == BinaryFormatConstants.Magic)
        {
            formatVersion = BitConverter.ToInt32(buffer[4..]);
            return true;
        }

        formatVersion = 0;
        return false;
    }
}