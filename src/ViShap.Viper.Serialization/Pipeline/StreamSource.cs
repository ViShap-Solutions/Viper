using System.IO.Pipelines;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// The pipeline's edge with a caller's source — a stream or a pipe. It moves bytes into memory, where
/// a <see cref="WireReader"/> decodes them, and it is the one place a failure of the source itself is
/// reported, as <see cref="BinaryStreamException"/> with the <see cref="IOException"/> preserved.
/// Cancellation is not a failure of the source and leaves as <see cref="OperationCanceledException"/>.
/// </summary>
internal static class StreamSource
{
    /// <summary>The position of <paramref name="source"/>.</summary>
    public static long Position(Stream source)
    {
        try
        {
            return source.Position;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to read the position of the source stream.", ex);
        }
    }

    /// <summary>The bytes <paramref name="source"/> holds from its position to its end.</summary>
    public static long Remaining(Stream source)
    {
        try
        {
            return Math.Max(0, source.Length - source.Position);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to read the length of the source stream.", ex);
        }
    }

    /// <summary>Moves <paramref name="source"/> to <paramref name="position"/>.</summary>
    public static void Seek(Stream source, long position)
    {
        try
        {
            source.Position = position;
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to position the source stream.", ex);
        }
    }

    /// <summary>Reads at most <paramref name="destination"/>'s length and returns how many bytes arrived.</summary>
    public static int Read(Stream source, Span<byte> destination)
    {
        try
        {
            return source.Read(destination);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to read from the underlying stream.", ex);
        }
    }

    /// <summary>Reads at most <paramref name="destination"/>'s length and returns how many bytes arrived.</summary>
    public static async ValueTask<int> ReadAsync(
        Stream source,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to read from the underlying stream.", ex);
        }
    }

    /// <summary>Waits for the pipe to hold more than it held when last examined.</summary>
    public static async ValueTask<ReadResult> ReadAsync(PipeReader source, CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new BinaryStreamException("Failed to read from the underlying pipe.", ex);
        }
    }
}
