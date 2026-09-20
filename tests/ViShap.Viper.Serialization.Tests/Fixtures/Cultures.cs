using System.Globalization;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Cultures chosen from what the host actually provides.
/// </summary>
/// <remarks>
/// Naming a culture outright ties a test to the host's globalization data: under
/// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c>, which is how a trimmed Linux image usually runs,
/// <see cref="CultureInfo.GetCultureInfo(string)"/> raises for every name but the empty one. A test
/// about the encoding of a culture has no business failing over that, so it asks the machine which
/// cultures exist instead of assuming one.
/// </remarks>
internal static class Cultures
{
    /// <summary>
    /// A specific culture this host supports, or the invariant culture when it supports none. Its
    /// name is what a payload carries, so a test asserts against that rather than against a literal.
    /// </summary>
    public static CultureInfo Specific { get; } =
        CultureInfo.GetCultures(CultureTypes.SpecificCultures).FirstOrDefault()
        ?? CultureInfo.InvariantCulture;
}
