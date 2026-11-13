using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters.Market;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Verification;

/// <summary>
/// One comparative cell's parameters: a library configuration and a dataset in its models. Its text is
/// what the exported results carry as a column (WL-17).
/// </summary>
public sealed class MarketCase
{
    internal MarketCase(string adapterId, string datasetId, bool partial)
    {
        AdapterId = adapterId;
        DatasetId = datasetId;
        Partial = partial;
    }

    internal string AdapterId { get; }

    internal string DatasetId { get; }

    /// <summary>The pair verified as <c>Partial</c>: contents restored, identity not (FAIR-11).</summary>
    internal bool Partial { get; }

    internal MarketAdapter Adapter => MarketRoster.Find(AdapterId);

    internal Dataset Dataset => MarketCorpus.Find(Adapter.Family, DatasetId);

    public override string ToString() => $"{AdapterId.Replace('_', ' ')} · {DatasetId}";
}

/// <summary>
/// The pairs the comparative suites may time: only those verification found <c>Supported</c> or
/// <c>Partial</c> (FAIR-25). <c>--verify</c> records them; a suite reads the record, so a benchmark
/// process does not repeat the probes, and computes it once itself when none exists. Each suite still
/// round-trips its own pair in its setup and refuses a pair that no longer verifies.
/// </summary>
internal static class MarketCases
{
    internal static string Directory { get; } = Path.Combine(Environment.Paths.Artifacts, "market");

    internal static string RecordPath => Path.Combine(Directory, "verified-pairs.tsv");

    /// <summary>T4's verified pairs, recorded apart so no suite of B2 can time a composed baseline.</summary>
    internal static string ProtectedRecordPath => Path.Combine(Directory, "verified-protected-pairs.tsv");

    private static readonly Lazy<IReadOnlyList<MarketCase>> Verified = new(() => Load(RecordPath));

    private static readonly Lazy<IReadOnlyList<MarketCase>> VerifiedProtected = new(() => Load(ProtectedRecordPath));

    /// <summary>Every verified pair of T0–T3, in roster and corpus order.</summary>
    internal static IReadOnlyList<MarketCase> All => Verified.Value;

    /// <summary>Every verified pair of T4, in roster and dataset order.</summary>
    internal static IReadOnlyList<MarketCase> Protected => VerifiedProtected.Value;

    internal static void Record(MarketVerification verification)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(RecordPath, Lines(verification.Pairs), Encoding.UTF8);
        File.WriteAllText(ProtectedRecordPath, Lines(verification.Protected), Encoding.UTF8);
    }

    private static string Lines(IEnumerable<MarketPairResult> pairs)
    {
        var text = new StringBuilder();

        foreach (var pair in pairs.Where(pair => pair.State is VerificationState.Supported or VerificationState.Partial))
            text.Append($"{pair.Adapter.Id}\t{pair.Dataset}\t{pair.State}\n");

        return text.ToString();
    }

    private static IReadOnlyList<MarketCase> Load(string path)
    {
        if (!File.Exists(path))
            Record(MarketVerifier.VerifyAll());

        return
        [
            .. File.ReadAllLines(path)
                .Where(line => line.Length > 0)
                .Select(line => line.Split('\t'))
                .Select(parts => new MarketCase(parts[0], parts[1], parts[2] == nameof(VerificationState.Partial))),
        ];
    }
}
