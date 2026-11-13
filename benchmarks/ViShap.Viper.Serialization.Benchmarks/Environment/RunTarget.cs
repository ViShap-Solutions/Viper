using System.Globalization;

namespace ViShap.Viper.Serialization.Benchmarks.Environment;

/// <summary>What kind of record a run produces.</summary>
internal enum RunKind
{
    /// <summary>
    /// Every suite of the track, on a tagged commit, under the publication job. This is the frozen
    /// record of a tagged revision — a release, a pre-release the owner asked to have measured, or a
    /// local tag taken before a rework — and it is never rewritten (Benchmark-Plan BASE-01, BASE-08).
    /// </summary>
    Baseline,

    /// <summary>
    /// Anything else: an untagged commit, a narrowed filter, or a shortened job. It carries only what
    /// it measured and says so, so it can never be read as a baseline (REP-12, REP-13).
    /// </summary>
    Measurement,
}

/// <summary>
/// Where a run writes, and why. The decision is made from git and from the run's own shape, never from
/// a flag that names a version: a baseline signed with the wrong version is worse than a missing one.
/// </summary>
/// <remarks>
/// A run writes into <c>WorkingDirectory</c>. A measurement's working directory is its directory; a
/// baseline is measured under <c>Measurements/</c> and moved to <c>Baselines/</c> only once every suite
/// has run (<see cref="Settle"/>), so a run that stops early — on request or by dying — never leaves a
/// partial record where a baseline belongs.
/// </remarks>
internal sealed record RunTarget(
    RunKind Kind,
    string Directory,
    string WorkingDirectory,
    string Label,
    string? Tag,
    string Describe,
    string Reason,
    bool Dirty,
    bool AllowDirty)
{
    /// <summary>
    /// The baselines directory as git names it from the repository's top, which <see cref="Dirty"/> leaves
    /// out: a baseline Track A wrote moments ago, not yet committed, is the output of this harness and not a
    /// change to what is measured, and Track B on the same revision is measured beside it.
    /// </summary>
    private const string BaselinesPathspec = Paths.ProjectName + "/Baselines";

    internal bool IsBaseline => Kind == RunKind.Baseline;

    /// <summary>
    /// When the run measures a tag from a later commit (<c>--as</c>), the tag; otherwise <c>null</c>, and
    /// <see cref="Tag"/> is the commit's own.
    /// </summary>
    internal string? MeasuredAs { get; init; }

    /// <summary>The files under <c>src/</c> that differ from <see cref="MeasuredAs"/>, none of them code.</summary>
    internal IReadOnlyList<string> SourceDifferences { get; init; } = [];

    /// <summary>
    /// Whether the working tree differs from the commit in anything but the baselines directory, which
    /// holds this harness's own output.
    /// </summary>
    internal static bool WorkingTreeChanged() =>
        !string.IsNullOrWhiteSpace(Shell.Git($"status --porcelain -- \":(top,exclude){BaselinesPathspec}\""));

    /// <summary>
    /// Decides the destination. A run is a baseline only when the commit carries a tag of its own, the
    /// job is the publication job, and no filter narrowed the suites; otherwise it is a measurement,
    /// named after `git describe` and the moment it started so that no two runs can collide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Track B is measured on the tag Track A's baseline already holds, and is kept beside it, in
    /// <c>Baselines/&lt;tag&gt;/market/</c>, without touching a file of it (Benchmark-Plan §29.2). Until
    /// that baseline exists a Track B run on the tag is a measurement.
    /// </para>
    /// <para>
    /// <paramref name="asTag"/> records a later commit as the baseline of an earlier tag when the library
    /// is the one that tag released and only the harness moved on (Q4): every file under <c>src/</c> that
    /// differs must be one that cannot change code (<see cref="SourceIdentity"/>), and the run records the
    /// commit it ran on beside the tag it is filed under.
    /// </para>
    /// </remarks>
    internal static RunTarget Decide(bool shortened, string? filter, bool allowDirty, bool market = false, string? asTag = null)
    {
        var tag = Shell.Git("describe --tags --exact-match HEAD");
        var describe = Shell.Git("describe --tags --always --long");

        // An empty answer means a clean tree or no git at all; outside a repository there is no tag
        // either, so a run there can never be a baseline and the distinction never matters.
        bool dirty = WorkingTreeChanged();
        IReadOnlyList<string> differences = [];
        string? measuredAs = null;

        if (string.IsNullOrEmpty(describe))
        {
            describe = "no-git";
        }

        // Every reason, not the first one found: a shortened run on an untagged commit is unusable
        // because of the job, and a reader told only about the missing tag would trust the numbers.
        var blocked = new List<string>();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var working = Path.Combine(Paths.Measurements, $"{describe}-{stamp}");

        if (asTag is not null && asTag != tag)
        {
            if (!SourceIdentity.TagExists(asTag))
            {
                blocked.Add($"--as names {asTag}, which is not a tag of this repository");
            }
            else
            {
                var comparison = SourceIdentity.Compare(asTag);

                if (comparison.SameCode)
                {
                    tag = asTag;
                    measuredAs = asTag;
                    differences = comparison.Differing;
                }
                else
                {
                    blocked.Add($"src/ differs from {asTag} in what may change code: {string.Join(", ", comparison.Code)}");
                }
            }
        }

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

        if (market && !string.IsNullOrEmpty(tag) && !System.IO.Directory.Exists(Path.Combine(Paths.Baselines, tag)))
        {
            blocked.Add($"Track A's baseline of {tag} does not exist yet, and Track B is measured beside it");
        }

        if (blocked.Count == 0)
        {
            return new RunTarget(
                RunKind.Baseline,
                market ? Path.Combine(Paths.Baselines, tag, "market") : Path.Combine(Paths.Baselines, tag),
                working,
                market ? $"{tag}/market" : tag,
                tag,
                describe,
                measuredAs is null
                    ? "every suite of the track, on a tagged commit, under the publication job"
                    : $"every suite of the track, under the publication job, on a commit whose src/ is {measuredAs}'s in code",
                dirty,
                allowDirty)
            {
                MeasuredAs = measuredAs,
                SourceDifferences = differences,
            };
        }

        return new RunTarget(
            RunKind.Measurement,
            working,
            working,
            Path.GetFileName(working),
            string.IsNullOrEmpty(tag) ? null : tag,
            describe,
            string.Join("; ", blocked),
            dirty,
            allowDirty)
        {
            MeasuredAs = measuredAs,
            SourceDifferences = differences,
        };
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
                $"The working tree has uncommitted changes outside Baselines/, so a baseline named {Tag} would not measure " +
                "what that tag points at. Commit or stash them, or pass --allow-dirty deliberately, which " +
                "records the flag in the run. Nothing was written.");
        }

        System.IO.Directory.CreateDirectory(WorkingDirectory);
    }

    /// <summary>
    /// What this run is once it stopped before its last suite: a measurement, in the directory it was
    /// written to, whatever it was meant to be. A baseline is every suite of the track, so a run that
    /// did not run them all is not one, under any tag.
    /// </summary>
    internal RunTarget Interrupted() => this with
    {
        Kind = RunKind.Measurement,
        Directory = WorkingDirectory,
        Label = Path.GetFileName(WorkingDirectory),
        Reason = IsBaseline
            ? "the run was interrupted before every suite of the track ran"
            : $"{Reason}; the run was interrupted",
    };

    /// <summary>
    /// Moves a complete baseline from the directory it was measured in to the one it is kept in, and
    /// returns the target that writes there. A measurement is already where it belongs.
    /// </summary>
    internal RunTarget Settle()
    {
        if (!IsBaseline || WorkingDirectory == Directory)
        {
            return this;
        }

        if (System.IO.Directory.Exists(Directory))
        {
            throw new InvalidOperationException(
                $"A baseline appeared at {Directory} while this run was measuring. It is frozen and was left " +
                $"as it is; this run stays at {WorkingDirectory}.");
        }

        System.IO.Directory.Move(WorkingDirectory, Directory);
        return this with { WorkingDirectory = Directory };
    }

    internal string File(string name) => Path.Combine(WorkingDirectory, name);
}
