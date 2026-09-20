using ViShap.Viper.Checksum;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A custom checksum an external consumer could plausibly write: one byte, the sum of the payload.
/// It exists to prove that registration reaches exactly one configuration, not that it detects much.
/// </summary>
internal sealed class Sum8 : IChecksumAlgorithm
{
    public const string RegisteredName = "sum8";

    public ChecksumAlgorithm Kind => ChecksumAlgorithm.Custom;

    public string? CustomName => RegisteredName;

    public int HashSizeInBytes => 1;

    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        byte sum = 0;
        foreach (byte value in source)
            sum += value;

        destination[0] = sum;
    }
}
