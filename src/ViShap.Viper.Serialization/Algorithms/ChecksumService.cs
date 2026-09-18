namespace ViShap.Viper.Checksum;

/// <summary>Computes and verifies the payload checksum. Internal for the same reason as the other services.</summary>
internal sealed class ChecksumService(IChecksumAlgorithm algorithm)
{
    public ChecksumAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;

    public byte[] Compute(byte[] rawPayload)
    {
        var destination = new byte[algorithm.HashSizeInBytes];
        algorithm.Compute(rawPayload, destination);
        return destination;
    }

    public static void Verify(
        IChecksumAlgorithm algorithm,
        byte[] rawPayload,
        byte[] expectedChecksum)
    {
        if (expectedChecksum.Length != algorithm.HashSizeInBytes)
            throw new BinaryFormatException(
                $"Checksum length {expectedChecksum.Length} does not match the " +
                $"{algorithm.HashSizeInBytes} byte(s) produced by '{algorithm.Kind}'.");

        Span<byte> actual = algorithm.HashSizeInBytes <= 64
            ? stackalloc byte[algorithm.HashSizeInBytes]
            : new byte[algorithm.HashSizeInBytes];

        algorithm.Compute(rawPayload, actual);

        if (!actual.SequenceEqual(expectedChecksum))
            throw new BinaryIntegrityException(
                "Checksum mismatch — the binary payload appears to be corrupted.");
    }
}
