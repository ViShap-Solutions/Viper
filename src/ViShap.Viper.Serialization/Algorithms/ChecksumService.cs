namespace ViShap.Viper.Checksum;

/// <summary>Computes and verifies the payload checksum. Internal for the same reason as the other services.</summary>
internal sealed class ChecksumService(IChecksumAlgorithm algorithm)
{
    public ChecksumAlgorithm Kind => algorithm.Kind;
    public string? CustomName => algorithm.CustomName;

    /// <summary>Computes the checksum of <paramref name="rawPayload"/>. Not called for <see cref="ChecksumAlgorithm.None"/>.</summary>
    public byte[] Compute(ReadOnlySpan<byte> rawPayload)
    {
        var destination = new byte[HashSize(algorithm)];
        algorithm.Compute(rawPayload, destination);
        return destination;
    }

    /// <summary>
    /// Checks <paramref name="expectedChecksum"/>, as the header recorded it, against the checksum of
    /// <paramref name="rawPayload"/>. A payload without a checksum must record none.
    /// </summary>
    public static void Verify(
        IChecksumAlgorithm algorithm,
        ReadOnlySpan<byte> rawPayload,
        byte[] expectedChecksum)
    {
        int size = algorithm.Kind == ChecksumAlgorithm.None ? 0 : HashSize(algorithm);

        if (expectedChecksum.Length != size)
            throw new BinaryFormatException(
                $"Checksum length {expectedChecksum.Length} does not match the " +
                $"{size} byte(s) produced by '{algorithm.Kind}'.");

        if (size == 0)
            return;

        Span<byte> actual = size <= 64 ? stackalloc byte[size] : new byte[size];

        algorithm.Compute(rawPayload, actual);

        if (!actual.SequenceEqual(expectedChecksum))
            throw new BinaryIntegrityException(
                "Checksum mismatch — the binary payload appears to be corrupted.");
    }

    /// <summary>
    /// The size a configured algorithm claims, checked once before it sizes a buffer. It is the one
    /// value an external implementation supplies that the serializer turns into an allocation; a
    /// checksum has at least one byte, and the V1 header can record at most 255.
    /// </summary>
    private static int HashSize(IChecksumAlgorithm algorithm)
    {
        int size = algorithm.HashSizeInBytes;
        if (size is < 1 or > byte.MaxValue)
            throw new BinaryConfigurationException(
                $"Checksum algorithm '{algorithm.CustomName ?? algorithm.Kind.ToString()}' reports a " +
                $"hash size of {size} byte(s), which cannot be represented by the V1 header: a checksum " +
                "is between 1 and 255 bytes.");

        return size;
    }
}
