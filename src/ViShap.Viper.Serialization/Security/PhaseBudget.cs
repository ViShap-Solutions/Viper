namespace ViShap.Viper.Security;

/// <summary>
/// Physical size policy for the pipeline phases. This is the single place where payload, compressed
/// and encrypted sizes are checked, which is why the algorithms themselves never see a limit.
/// </summary>
internal readonly struct PhaseBudget(SerializationLimits limits)
{
    private readonly SerializationLimits _limits = limits;

    public void CheckPayload(long length, string what) =>
        Check(length, _limits.MaxPayloadBytes, what);

    public void CheckCompressed(long length, string what) =>
        Check(length, _limits.MaxCompressedBytes, what);

    public void CheckEncrypted(long length, string what) =>
        Check(length, _limits.MaxEncryptedBytes, what);

    private static void Check(long length, long maximum, string what)
    {
        if (length < 0)
            throw new BinaryFormatException($"{what} {length} must be non-negative.");

        if (length > maximum)
            throw new BinaryLimitException(
                $"{what} {length} exceeds the configured maximum of {maximum}.");
    }
}
