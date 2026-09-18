namespace ViShap.Viper.Metadata;

public static class BinaryFormatInspector
{
    public static BinaryHeaderInfo? Peek(Stream source)
        => Peek(source, SerializationLimits.Default);

    internal static BinaryHeaderInfo? Peek(Stream source, SerializationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        if (!source.CanSeek)
            throw new NotSupportedException($"{nameof(Peek)} needs a seekable stream.");

        long start = source.Position;
        try
        {
            if (!BinaryHeaderPeek.TryPeekMagicAndVersion(source, out int version))
                return null;

            using var budgeted = new BudgetedReadStream(
                source,
                limits.MaxWireBytes,
                "format inspection",
                leaveOpen: true);

            return CodecRegistry.Inspect(budgeted, version, limits);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException(
                "Failed to inspect the binary format from the underlying stream.", ex);
        }
        finally
        {
            try
            {
                source.Position = start;
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException(
                    "Failed to restore the source stream position after format inspection.", ex);
            }
        }
    }
}