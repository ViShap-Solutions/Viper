using System.Buffers;

namespace ViShap.Viper.Compression;

public sealed class Compressor : ICompressor
{
    private readonly ICompressionAlgorithm _defaultAlgorithm;
    private readonly SerializationLimits _limits;

    public Compressor(ICompressionAlgorithm algorithm, SerializationLimits? limits = null)
    {
        _defaultAlgorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _limits = limits ?? SerializationLimits.Default;
        _limits.Validate();
    }

    private Compressor() : this(new NoCompression()) { }
    public static Compressor None { get; } = new();

    public CompressionAlgorithm DefaultKind => _defaultAlgorithm.Kind;
    public string? DefaultCustomName => _defaultAlgorithm.CustomName;

    public byte[] Compress(byte[] rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        if (rawPayload.LongLength > _limits.MaxPayloadBytes)
            throw new BinaryLimitException(
                $"Payload length {rawPayload.LongLength} exceeds the configured maximum of {_limits.MaxPayloadBytes}.");

        if (_defaultAlgorithm.Kind == CompressionAlgorithm.None)
        {
            if (rawPayload.LongLength > _limits.MaxCompressedBytes)
                throw new BinaryLimitException(
                    $"Compressed payload length {rawPayload.LongLength} exceeds the configured maximum of {_limits.MaxCompressedBytes}.");
            return rawPayload;
        }

        int maxLength = _defaultAlgorithm.GetMaxCompressedLength(rawPayload.Length);
        if (maxLength < 0)
            throw new InvalidOperationException(
                "The compression algorithm returned a negative maximum compressed length.");

        long configuredMaximum = _limits.MaxCompressedBytes;
        int destinationLength = (int)Math.Min((long)maxLength, configuredMaximum);
        bool destinationWasCapped = destinationLength < maxLength;

        byte[] rented = ArrayPool<byte>.Shared.Rent(destinationLength);
        try
        {
            int written;
            try
            {
                written = _defaultAlgorithm.Compress(rawPayload, rented);
            }
            catch (InvalidOperationException ex) when (destinationWasCapped)
            {
                throw new BinaryLimitException(
                    $"Compressed payload could not fit within the configured maximum of {_limits.MaxCompressedBytes} bytes.", ex);
            }
            catch (BinaryFormatException ex) when (destinationWasCapped)
            {
                throw new BinaryLimitException(
                    $"Compressed payload could not fit within the configured maximum of {_limits.MaxCompressedBytes} bytes.", ex);
            }

            if (written < 0 || written > rented.Length)
                throw new InvalidOperationException(
                    $"The compression algorithm returned an invalid output length of {written}.");

            if (written > _limits.MaxCompressedBytes)
                throw new BinaryLimitException(
                    $"Compressed payload length {written} exceeds the configured maximum of {_limits.MaxCompressedBytes}.");

            return rented.AsSpan(0, written).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    public byte[] Decompress(
        CompressionAlgorithm kind,
        string? customName,
        byte[] compressedPayload,
        int uncompressedLength)
    {
        ArgumentNullException.ThrowIfNull(compressedPayload);
        if (uncompressedLength < 0)
            throw new BinaryFormatException(
                $"Declared uncompressed length {uncompressedLength} must be non-negative.");

        if (uncompressedLength > _limits.MaxPayloadBytes)
            throw new BinaryLimitException(
                $"Declared uncompressed length {uncompressedLength} exceeds the configured maximum of {_limits.MaxPayloadBytes}.");

        if (compressedPayload.LongLength > _limits.MaxCompressedBytes)
            throw new BinaryLimitException(
                $"Compressed payload length {compressedPayload.LongLength} exceeds the configured maximum of {_limits.MaxCompressedBytes}.");

        if (kind == CompressionAlgorithm.None)
        {
            if (compressedPayload.Length != uncompressedLength)
                throw new BinaryFormatException(
                    $"Compressed payload length {compressedPayload.Length} does not match the declared uncompressed length {uncompressedLength} when compression is None.");
            return compressedPayload;
        }

        var algorithm = CompressionAlgorithmRegistry.Resolve(kind, customName);
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

        if (written != uncompressedLength)
        {
            Array.Clear(result);
            throw new BinaryFormatException(
                $"Decompression produced {written} bytes, expected {uncompressedLength}.");
        }

        return result;
    }
}