using System.Globalization;

namespace ViShap.Viper.Serialization.Benchmarks.Environment;

/// <summary>What kind of record a run produces.</summary>
internal enum RunKind
{
    /// <summary>
    /// Every suite of the track, on a tagged commit, under the publication job. This is the frozen
    /// record of a released version, and it is never rewritten (Benchmark-Plan BASE-01, BASE-08).
    /// </summary>
    Baseline,

    /// <summary>
    /// Anything else: an untagged commit, a narrowed filter, or a shortened job. It carries only what
    /// it measured and says so, so it can never be read as a baseline (REP-12, REP-13).
    /// </summary>
    Partial,
}

/// <summary>
/// Where a run writes, and why. The decision is made from git and from the run's own shape, never from
/// a flag that names a version: a baseline signed with the wrong version is worse than a missing one.
/// </summary>
internal sealed record RunTarget(
    RunKind Kind,
    string Directory,
    string Label,
    string? Tag,
    string Describe,
    string Reason,
    bool Dirty,
    bool AllowDirty)
{
    internal bool IsBaseline => Kind == RunKind.Baseline;

    /// <summary>
    /// Decides the destination. A run is a baseline only when the commit carries a tag of its own, the
    /// job is the publication job, and no filter narrowed the suites; otherwise it is a partial run,
    /// named after `git describe` and the moment it started so that no two runs can collide.
    /// </summary>
    internal static RunTarget Decide(bool shortened, string? filter, bool allowDirty)
    {
        var tag = Shell.Git("describe --tags --exact-match HEAD");
        var describe = Shell.Git("describe --tags --always --long");

        // An empty answer means a clean tree or no git at all; outside a repository there is no tag
        // either, so a run there can never be a baseline and the distinction never matters.
        bool dirty = !string.IsNullOrWhiteSpace(Shell.Git("status --porcelain"));

        if (string.IsNullOrEmpty(describe))
        {
            describe = "no-git";
        }

        // Every reason, not the first one found: a shortened run on an untagged commit is unusable
        // because of the job, and a reader told only about the missing tag would trust the numbers.
        var blocked = new List<string>();

        if (string.IsNullOrEmpty(tag))
        {
            blocked.Add("HEAD carries no tag");
        }

        if (shortened)
        {
            blocked.Add("the job is shortened, so these are not publication numbers");
        }

        if (filter is not null)
        {
            blocked.Add($"a filter narrowed the run to '{filter}'");
        }

        if (blocked.Count == 0)
        {
            return new RunTarget(
                RunKind.Baseline,
                Path.Combine(Paths.Baselines, tag),
                tag,
                tag,
                describe,
                "every suite of the track, on a tagged commit, under the publication job",
                dirty,
                allowDirty);
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var label = $"{describe}-{stamp}";

        return new RunTarget(
            RunKind.Partial,
            Path.Combine(Paths.Measurements, label),
            label,
            string.IsNullOrEmpty(tag) ? null : tag,
            describe,
            string.Join("; ", blocked),
            dirty,
            allowDirty);
    }

    /// <summary>
    /// Creates the directory. A baseline that already exists is never touched: a re-measurement of the
    /// same tag is a separate decision the owner makes, not something a run does by overwriting
    /// (REP-09).
    /// </summary>
    internal void Create()
    {
        if (IsBaseline && System.IO.Directory.Exists(Directory))
        {
            throw new InvalidOperationException(
                $"A baseline already exists at {Directory}. It is frozen: delete it deliberately, or " +
                "re-measure under a new tag. Nothing was written.");
        }

        // A baseline signed with a tag must measure what the tag points at. A dirty tree measures
        // something else under that name, which is the same forgery BASE-08 refuses from the other side —
        // and after three hours of measuring, discovering it at the end would be worse than failing now.
        if (IsBaseline && Dirty && !AllowDirty)
        {
            throw new InvalidOperationException(
                $"The working tree has uncommitted changes, so a baseline named {Tag} would not measure " +
                "what that tag points at. Commit or stash them, or pass --allow-dirty deliberately, which " +
                "records the flag in the run. Nothing was written.");
        }

        System.IO.Directory.CreateDirectory(Directory);
    }

    internal string File(string name) => Path.Combine(Directory, name);
}
