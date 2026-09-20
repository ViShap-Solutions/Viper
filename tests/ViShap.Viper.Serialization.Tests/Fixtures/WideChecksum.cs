using ViShap.Viper.Checksum;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A custom checksum of a caller-chosen width, used to reach the edges of the one-byte
/// <c>checksumLength</c> field the V1 header carries. The digest is deterministic and detects a
/// changed payload, which is all the suites that use it need.
/// </summary>
internal sealed class WideChecksum(int size) : IChecksumAlgorithm
{
    public const string RegisteredName = "wide";

    public ChecksumAlgorithm Kind => ChecksumAlgorithm.Custom;

    public string? CustomName => RegisteredName;

    public int HashSizeInBytes => size;

    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        byte sum = 0;
        foreach (byte value in source)
            sum = (byte)(sum * 31 + value);

        for (int i = 0; i < destination.Length; i++)
            destination[i] = (byte)(sum + i);
    }
}
