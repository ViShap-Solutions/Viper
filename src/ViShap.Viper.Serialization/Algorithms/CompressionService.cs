using System.Buffers;

namespace ViShap.Viper.Compression;

/// <summary>
/// Runs a compression algorithm. It is internal on purpose: phase sizes are checked by the pipeline
/// that calls it, so the barrier cannot be replaced from outside, and an algorithm implementation
/// never has to know a limit.
/// </summary>
internal sealed class CompressionService(ICompressionAlgorithm algorithm)
{
    public CompressionAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;

    public byte[] Compress(byte[] rawPayload, long maxCompressedBytes)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        if (algorithm.Kind == CompressionAlgorithm.None)
            return rawPayload;

        int maxLength = algorithm.GetMaxCompressedLength(rawPayload.Length);
        if (maxLength < 0)
            throw new BinaryConfigurationException(
                "The compression algorithm returned a negative maximum compressed length.");

        int destinationLength = (int)Math.Min(maxLength, maxCompressedBytes);
        bool capped = destinationLength < maxLength;

        byte[] rented = ArrayPool<byte>.Shared.Rent(destinationLength);
        try
        {
            int written;
            try
            {
                written = algorithm.Compress(rawPayload, rented.AsSpan(0, destinationLength));
            }
            catch (Exception ex) when (capped && ex is InvalidOperationException or BinaryFormatException)
            {
                throw new BinaryLimitException(
                    $"Compressed payload could not fit within the configured maximum of " +
                    $"{maxCompressedBytes} bytes.", ex);
            }

            if (written < 0 || written > destinationLength)
                throw new BinaryConfigurationException(
                    $"The compression algorithm returned an invalid output length of {written}.");

            return rented.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    /// <summary>
    /// Decompresses into a buffer of exactly the declared size. The algorithm must produce that many
    /// bytes and no more — a stream that expands further is rejected rather than silently truncated.
    /// </summary>
    public static byte[] Decompress(
        ICompressionAlgorithm algorithm,
        byte[] compressedPayload,
        int uncompressedLength)
    {
        ArgumentNullException.ThrowIfNull(compressedPayload);

        if (algorithm.Kind == CompressionAlgorithm.None)
        {
            if (compressedPayload.Length != uncompressedLength)
                throw new BinaryFormatException(
                    $"Compressed payload length {compressedPayload.Length} does not match the declared " +
                    $"uncompressed length {uncompressedLength} when compression is None.");

            return compressedPayload;
        }

        var result = new byte[uncompressedLength];
        int written;
        try
        {
            written = algorithm.Decompress(compressedPayload, result);
        }
        catch (InvalidDataException ex)
        {
            Array.Clear(result);
            throw new BinaryFormatException(
                "Decompression failed because the compressed payload is malformed.", ex);
        }
        catch (BinarySerializerException)
        {
            Array.Clear(result);
            throw;
        }

        if (written != uncompressedLength)
        {
            Array.Clear(result);
            throw new BinaryFormatException(
                $"Decompression produced {written} bytes, expected {uncompressedLength}.");
        }

        return result;
    }
}
