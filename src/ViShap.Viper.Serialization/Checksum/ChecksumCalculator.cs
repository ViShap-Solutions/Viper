namespace ViShap.Viper.Checksum;

public sealed class ChecksumCalculator(IChecksumAlgorithm algorithm) : IChecksumCalculator
{
    private readonly IChecksumAlgorithm _defaultAlgorithm = algorithm;

    private ChecksumCalculator() : this(new NoChecksum()) { }
    public static ChecksumCalculator None { get; } = new();

    public ChecksumAlgorithm DefaultKind => _defaultAlgorithm.Kind;
    public string? DefaultCustomName => _defaultAlgorithm.CustomName;
    
    public byte[] Compute(byte[] rawPayload)
    {
        var destination = new byte[_defaultAlgorithm.HashSizeInBytes];
        _defaultAlgorithm.Compute(rawPayload, destination);
        return destination;
    }

    public void Verify(ChecksumAlgorithm kind, string? customName, byte[] rawPayload, byte[] expectedChecksum)
    {
        var algorithm = ChecksumAlgorithmRegistry.Resolve(kind, customName);

        var actual = new byte[algorithm.HashSizeInBytes];
        algorithm.Compute(rawPayload, actual);

        if (!actual.AsSpan().SequenceEqual(expectedChecksum))
            throw new BinaryIntegrityException("Checksum mismatch — the binary payload appears to be corrupted.");
    }
}