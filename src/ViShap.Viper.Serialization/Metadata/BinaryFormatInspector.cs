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
        catch (EndOfStreamException ex)
        {
            throw new BinaryFormatException(
                "Binary data ended unexpectedly while reading the format header.", ex);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to read the binary format header from the source stream.", ex);
        }
        finally
        {
            source.Position = start;
        }
    }
}
