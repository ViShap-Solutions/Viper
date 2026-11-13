namespace ViShap.Viper.Serialization.Benchmarks.Environment;

/// <summary>
/// The version of Viper a run measures, as its reports name it. The harness builds Viper from source,
/// where no version is set — a package takes its version from its tag at publication — so the assembly
/// says <c>1.0.0</c> whatever the code is. The version is therefore the run's: the tag it is filed under
/// (its own, or the one <c>--as</c> names) without the leading <c>v</c>, and otherwise what
/// <c>git describe</c> says, which names an untagged commit for what it is.
/// </summary>
internal static class ViperVersion
{
    private static string? _value;

    /// <summary>The version; read by the market roster, so it is set before the roster is first built.</summary>
    internal static string Value => _value ??= Of(
        Shell.Git("describe --tags --exact-match HEAD") is { Length: > 0 } tag ? tag : Shell.Git("describe --tags --always --long"));

    /// <summary>Fixes the version to the one a run is filed under.</summary>
    internal static void Set(string? tag, string describe) => _value = Of(tag ?? describe);

    private static string Of(string revision) =>
        revision.Length > 1 && revision[0] == 'v' && char.IsDigit(revision[1]) ? revision[1..] : revision is { Length: > 0 } ? revision : "unknown";
}
