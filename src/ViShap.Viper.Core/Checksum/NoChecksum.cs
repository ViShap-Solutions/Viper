namespace ViShap.Viper.Checksum;

public sealed class NoChecksum : IChecksumAlgorithm
{
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.None;
    public string? CustomName => null;
    public int HashSizeInBytes => 0;
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) { }
}