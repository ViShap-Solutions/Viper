using System.Buffers;

namespace ViShap.Viper.Pipeline;

/// <summary>
/// The pipeline's edge with a caller's seekable source stream. It moves bytes from the stream into
/// memory, where a <see cref="WireReader"/> decodes them, and puts the stream's position where the
/// decoded bytes end. A failure of the stream itself is reported as
/// <see cref="BinaryStreamException"/>.
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

    /// <summary>
    /// Reads until <paramref name="destination"/> is full or the source ends, and returns how many
    /// bytes were read.
    /// </summary>
    public static int Read(Stream source, Span<byte> destination, string what)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read;
            try
            {
                read = source.Read(destination[total..]);
            }
            catch (IOException ex)
            {
                throw new BinaryStreamException($"Failed to read {what} from the underlying stream.", ex);
            }

            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    /// <summary>
    /// Reads ahead up to <paramref name="maximum"/> bytes into a buffer rented from the pool. The caller
    /// returns it through <see cref="Return"/>, which clears it.
    /// </summary>
    /// <param name="source">The source, positioned at the first byte to read.</param>
    /// <param name="maximum">The most bytes to take, such as the budget of the operation.</param>
    /// <param name="what">The bytes being read, used in diagnostics.</param>
    /// <param name="length">The number of bytes actually read.</param>
    public static byte[] ReadAhead(Stream source, long maximum, string what, out int length)
    {
        int size = (int)Math.Min(Math.Min(Remaining(source), maximum), Array.MaxLength);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
        bool completed = false;
        try
        {
            length = Read(source, buffer.AsSpan(0, size), what);
            completed = true;
            return buffer;
        }
        finally
        {
            if (!completed)
                Return(buffer);
        }
    }

    /// <summary>Clears a buffer taken from <see cref="ReadAhead"/> and returns it to the pool.</summary>
    public static void Return(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
}
