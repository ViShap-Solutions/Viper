namespace ViShap.Viper.Checksum;

public interface IChecksumCalculator
{
    ChecksumAlgorithm DefaultKind { get; }
    string? DefaultCustomName { get; }
    byte[] Compute(byte[] rawPayload);
    void Verify(ChecksumAlgorithm kind, string? customName, byte[] rawPayload, byte[] expectedChecksum);
}