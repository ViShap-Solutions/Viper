namespace ViShap.Viper.Metadata;

public static class BinaryFormatInspector
{
    public static BinaryHeaderInfo? Peek(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
            throw new NotSupportedException($"{nameof(Peek)} needs a seekable stream.");

        long start = source.Position;
        try
        {
            if (!BinaryHeaderPeek.TryPeekMagicAndVersion(source, out int version))
                return null;

            return CodecRegistry.Inspect(source, version);
        }
        finally
        {
            source.Position = start;
        }
    }
}
