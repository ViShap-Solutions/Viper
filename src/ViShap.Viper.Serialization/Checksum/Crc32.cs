namespace ViShap.Viper.Checksum;

public sealed class Crc32 : IChecksumAlgorithm
{
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.Crc32;
    public string? CustomName => null;
    public int HashSizeInBytes => 4;
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) =>
        System.IO.Hashing.Crc32.Hash(source, destination);
}