namespace ViShap.Viper.Format;

internal static class BinaryFormatConstants
{
    public const int Magic = 0x52455342;
    public const int LatestVersion = 1;

    /// <summary>
    /// The byte length a string in a V1 header may take. Header strings name an algorithm or select a
    /// key, so the ceiling is a fixed property of the format rather than a configured policy.
    /// </summary>
    public const int MaxHeaderStringBytes = 256;
}