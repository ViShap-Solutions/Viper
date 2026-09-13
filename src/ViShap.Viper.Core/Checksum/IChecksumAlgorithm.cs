namespace ViShap.Viper.Checksum;

public interface IChecksumAlgorithm
{
    ChecksumAlgorithm Kind { get; }
    string? CustomName { get; }
    int HashSizeInBytes { get; }
    void Compute(ReadOnlySpan<byte> source, Span<byte> destination);
}