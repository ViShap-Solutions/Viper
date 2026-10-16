using System.Reflection;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// The configuration rationale of FAIR-13, published verbatim in the report: the mode, the options and
/// where they come from, what was deliberately left off, and a faster mode not used.
/// </summary>
internal sealed record Rationale(string Mode, string Options, string NotEnabled, string FasterModeNotUsed);

/// <summary>
/// What a library does instead of round-tripping a dataset, declared by its adapter. It never sets a
/// result on its own: verification accepts it only when the difference it observes contains
/// <see cref="Observed"/>, and the pair is then <c>Unsupported</c> with <see cref="Reason"/> (FAIR-09,
/// DATA-24). A pair that fails any other way is <c>Failed</c>.
/// </summary>
/// <param name="Dataset">A dataset id, or its prefix before the slash for every variant of it.</param>
/// <param name="Observed">A fragment of the difference or the exception verification reports.</param>
/// <param name="Reason">What the library cannot express, in one sentence.</param>
internal sealed record Limitation(string Dataset, string Observed, string Reason)
{
    internal bool Covers(string datasetId, string detail) =>
        (datasetId == Dataset || datasetId.Split('/')[0] == Dataset)
        && detail.Contains(Observed, StringComparison.Ordinal);
}

/// <summary>
/// A library in one configuration, measured through its public API in its own documented best mode
/// (Benchmark-Plan §5.2, §7.3). The serializer, resolver, context or model is built once in the
/// constructor, outside every timed region (FAIR-03).
/// </summary>
internal abstract class MarketAdapter : IBufferedSerializer, IStreamingSerializer
{
    protected MarketAdapter(string library, Assembly assembly, string configuration, ModelFamily family, Tier intended)
    {
        Library = library;
        Version = VersionOf(assembly);
        Configuration = configuration;
        Family = family;
        Intended = intended;
    }

    /// <summary>The library's name as the roster of §5.2 writes it.</summary>
    internal string Library { get; }

    /// <summary>The informational version of the assembly the adapter calls, as loaded.</summary>
    internal string Version { get; }

    /// <summary>The configuration's name within the library, unique for it.</summary>
    internal string Configuration { get; }

    /// <summary>The models this configuration serializes.</summary>
    internal ModelFamily Family { get; }

    /// <summary>
    /// The tiers this configuration exists for. Membership is decided by the probes (TIER-04); a tier
    /// listed here and not confirmed by them is left out of that tier's table.
    /// </summary>
    internal Tier Intended { get; }

    /// <summary>
    /// The configuration compresses its payload, so its cells are compared only with other compressed
    /// configurations (FAIR-16).
    /// </summary>
    internal bool Compressed { get; init; }

    public string Name => $"{Library} {Configuration}";

    /// <summary>The name without spaces, for a command line and a file.</summary>
    internal string Id => Name.Replace(' ', '_');

    internal abstract Rationale Rationale { get; }

    internal virtual IReadOnlyList<Limitation> Limitations => [];

    public abstract byte[] Serialize<T>(T value);

    public abstract T? Deserialize<T>(byte[] payload);

    public abstract void Serialize<T>(Stream destination, T value);

    public abstract T? Deserialize<T>(Stream source);

    /// <summary>
    /// The envelope self-description probe: a value written with a production setting other than the
    /// default, read by an instance of the library configured with nothing. A library with no production
    /// setting returns <c>Unsupported</c> and names what its payload carries instead.
    /// </summary>
    internal abstract ProbeOutcome SelfDescription();

    /// <summary>
    /// Reads <paramref name="payload"/> into <paramref name="target"/> through the library's own
    /// in-place entry point and returns the instance it filled, or <see langword="null"/> when the
    /// library has none.
    /// </summary>
    internal virtual object? Populate<T>(byte[] payload, T target) where T : class => null;

    public override string ToString() => Name;

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        // The version alone: some packages append build metadata after a '+', others a sentence after a
        // space ("10.4.0. Commit Hash: …").
        int end = informational.IndexOfAny(['+', ' ']);
        return (end < 0 ? informational : informational[..end]).TrimEnd('.');
    }
}
