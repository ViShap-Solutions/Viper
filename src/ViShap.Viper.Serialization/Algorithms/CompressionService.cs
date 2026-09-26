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
                    $"{maxCompressedBytes} bytes " +
                    $"({nameof(SerializationLimits.MaxCompressedBytes)}).", ex);
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
    /// Produces exactly the declared number of bytes. The algorithm must produce that many and no
    /// more — a stream that expands further is rejected rather than silently truncated.
    /// <para>
    /// An algorithm that decompresses incrementally is driven through a buffer that grows as output
    /// arrives, so the declared length bounds the result without being allocated up front. One that
    /// offers only the span overload needs the whole buffer before it runs; the declared length is
    /// still bounded by <c>MaxPayloadBytes</c> and by <c>MaxDecompressionRatio</c>, which the
    /// pipeline checks before calling here.
    /// </para>
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

        return algorithm.SupportsIncrementalDecompression
            ? Incrementally(algorithm, compressedPayload, uncompressedLength)
            : AtOnce(algorithm, compressedPayload, uncompressedLength);
    }

    private static byte[] Incrementally(
        ICompressionAlgorithm algorithm,
        byte[] compressedPayload,
        int uncompressedLength)
    {
        using var buffer = new PayloadBufferWriter(uncompressedLength);

        int written;
        try
        {
            written = algorithm.Decompress(compressedPayload, buffer, uncompressedLength);
        }
        catch (InvalidDataException ex)
        {
            throw new BinaryFormatException(
                "Decompression failed because the compressed payload is malformed.", ex);
        }

        if (written != uncompressedLength || buffer.WrittenCount != uncompressedLength)
            throw new BinaryFormatException(
                $"Decompression produced {Math.Min(written, buffer.WrittenCount)} bytes, " +
                $"expected {uncompressedLength}.");

        return buffer.DetachPayload();
    }

    private static byte[] AtOnce(
        ICompressionAlgorithm algorithm,
        byte[] compressedPayload,
        int uncompressedLength)
    {
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
