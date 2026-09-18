namespace ViShap.Viper.Security;

/// <summary>
/// A read source that knows how many bytes it can still yield. Every declared length coming off the
/// wire is compared against this before it is allowed to drive an allocation.
/// <para>
/// The source also classifies the failure, because "this payload is shorter than it claims" and
/// "this payload would exceed a configured ceiling" are different contract violations:
/// <see cref="BinaryFormatException"/> versus <see cref="BinaryLimitException"/>.
/// </para>
/// </summary>
internal interface IRemainingBytes
{
    long RemainingBytes { get; }

    BinaryFormatException Exceeded(long requested, string what);
}
