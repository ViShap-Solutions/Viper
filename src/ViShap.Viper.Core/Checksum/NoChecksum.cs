namespace ViShap.Viper.Checksum;

/// <summary>
/// The no-op checksum algorithm, used when integrity checking is disabled. It produces a zero-length
/// value, so payloads carry no checksum bytes.
/// </summary>
public sealed class NoChecksum : IChecksumAlgorithm
{
    /// <inheritdoc />
    public ChecksumAlgorithm Kind => ChecksumAlgorithm.None;

    /// <inheritdoc />
    public string? CustomName => null;

    /// <inheritdoc />
    public int HashSizeInBytes => 0;

    /// <inheritdoc />
    public void Compute(ReadOnlySpan<byte> source, Span<byte> destination) { }
}
