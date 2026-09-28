namespace ViShap.Viper.Security;

/// <summary>
/// Physical size policy for the pipeline phases. This is the single place where payload, compressed
/// and encrypted sizes are checked, which is why the algorithms themselves never see a limit.
/// </summary>
internal readonly struct PhaseBudget(SerializationLimits limits)
{
    private readonly SerializationLimits _limits = limits;

    /// <summary>Whether <paramref name="length"/> is a payload length <see cref="CheckPayload"/> accepts.</summary>
    public bool AdmitsPayload(long length) => length >= 0 && length <= _limits.MaxPayloadBytes;

    public void CheckPayload(long length, string what) =>
        Check(length, _limits.MaxPayloadBytes, nameof(SerializationLimits.MaxPayloadBytes), what);

    public void CheckCompressed(long length, string what) =>
        Check(length, _limits.MaxCompressedBytes, nameof(SerializationLimits.MaxCompressedBytes), what);

    public void CheckEncrypted(long length, string what) =>
        Check(length, _limits.MaxEncryptedBytes, nameof(SerializationLimits.MaxEncryptedBytes), what);

    private static void Check(long length, long maximum, string limit, string what)
    {
        if (length < 0)
            throw new BinaryFormatException($"{what} {length} must be non-negative.");

        if (length > maximum)
            throw new BinaryLimitException(
                $"{what} {length} exceeds the configured maximum of {maximum} ({limit}).");
    }
}
