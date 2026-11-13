using System.Text.Json;

namespace ViShap.Viper.Serialization.Benchmarks.Environment;

/// <summary>
/// RST-03 — every pinned package against the latest stable version nuget.org lists on the day of the
/// run, so a comparison against an outdated competitor is visible in the run that made it. Best-effort:
/// without the network the field says so and the run goes on.
/// </summary>
internal static class PackageCurrency
{
    private const string Index = "https://api.nuget.org/v3-flatcontainer/{0}/index.json";

    /// <summary>
    /// One package: the version the project pins, the newest stable one of the same major version — a
    /// framework package follows the target framework's major, so a newer major is a choice, not staleness —
    /// and the newest stable one overall.
    /// </summary>
    internal sealed record Entry(string Pinned, string? Latest, string? LatestOverall, bool Current);

    internal static IReadOnlyDictionary<string, Entry> Check(IReadOnlyDictionary<string, string> pinned)
    {
        var result = new SortedDictionary<string, Entry>(StringComparer.Ordinal);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            foreach (var (id, version) in pinned)
            {
                var listed = Stable(http, id);
                var major = version.Split('.')[0] + ".";
                var latest = listed.LastOrDefault(candidate => candidate.StartsWith(major, StringComparison.Ordinal));
                result[id] = new Entry(version, latest, listed.LastOrDefault(), latest is null || latest == version);
            }
        }
        catch (Exception)
        {
            // No network, or nuget.org unreachable: the entries read so far stand, the rest are absent.
        }

        return result;
    }

    /// <summary>
    /// The measured packages that have a newer stable version than the one pinned. The Roslyn package is
    /// left out: it is pinned only to align the version the competitors' generators resolve (NU1608), and
    /// measures nothing, so its record stays in the manifest without a warning on every run.
    /// </summary>
    internal static IReadOnlyList<string> Outdated(IReadOnlyDictionary<string, Entry> entries) =>
    [
        .. entries
            .Where(entry => !entry.Value.Current && !entry.Key.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            .Select(entry => $"{entry.Key} {entry.Value.Pinned} → {entry.Value.Latest}"),
    ];

    /// <summary>Every stable version listed, in the index's own ascending order.</summary>
    private static List<string> Stable(HttpClient http, string id)
    {
        try
        {
            using var document = JsonDocument.Parse(
                http.GetStringAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, Index, id.ToLowerInvariant()))
                    .GetAwaiter().GetResult());

            return
            [
                .. document.RootElement.GetProperty("versions").EnumerateArray()
                    .Select(version => version.GetString() ?? string.Empty)
                    .Where(version => version.Length > 0 && !version.Contains('-', StringComparison.Ordinal)),
            ];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
