namespace ViShap.Viper.Checksum;

/// <summary>Computes and verifies the payload checksum. Internal for the same reason as the other services.</summary>
internal sealed class ChecksumService(IChecksumAlgorithm algorithm)
{
    public ChecksumAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;

    public byte[] Compute(byte[] rawPayload)
    {
        var destination = new byte[HashSize(algorithm)];
        algorithm.Compute(rawPayload, destination);
        return destination;
    }

    public static void Verify(
        IChecksumAlgorithm algorithm,
        byte[] rawPayload,
        byte[] expectedChecksum)
    {
        int size = HashSize(algorithm);

        if (expectedChecksum.Length != size)
            throw new BinaryFormatException(
                $"Checksum length {expectedChecksum.Length} does not match the " +
                $"{size} byte(s) produced by '{algorithm.Kind}'.");

        Span<byte> actual = size <= 64 ? stackalloc byte[size] : new byte[size];

        algorithm.Compute(rawPayload, actual);

        if (!actual.SequenceEqual(expectedChecksum))
            throw new BinaryIntegrityException(
                "Checksum mismatch — the binary payload appears to be corrupted.");
    }

    /// <summary>
    /// The size a configured algorithm claims, checked once before it sizes a buffer. It is the one
    /// value an external implementation supplies that the serializer turns into an allocation, and
    /// the V1 header can record at most 255 bytes of checksum.
    /// </summary>
    private static int HashSize(IChecksumAlgorithm algorithm)
    {
        int size = algorithm.HashSizeInBytes;
        if (size is < 0 or > byte.MaxValue)
            throw new BinaryConfigurationException(
                $"Checksum algorithm '{algorithm.CustomName ?? algorithm.Kind.ToString()}' reports a " +
                $"hash size of {size} byte(s), which cannot be represented by the V1 header.");

        return size;
    }
}
