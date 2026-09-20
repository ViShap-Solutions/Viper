using System.Text.Json;
using System.Text.Json.Serialization;
using ViShap.Viper.Serialization.Benchmarks.Environment;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// What a run was, written beside its results as <c>run.json</c>. The report is a view over the raw
/// files and over nothing else (REP-02), so the identity of the run has to be one of them: a report
/// regenerated from a committed baseline a year later says the same thing as the one generated the day
/// it was taken.
/// </summary>
internal sealed record RunRecord(
    string Kind,
    string Label,
    string? Tag,
    string Describe,
    string Revision,
    bool Dirty,
    bool AllowDirty,
    string Reason,
    string Track,
    string Job,
    string? Filter,
    string StartedUtc,
    string FinishedUtc,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> SuitesRun,
    IReadOnlyList<string> SuitesNotRun)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal bool IsBaseline => Kind == nameof(RunKind.Baseline);

    internal void Write(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Format));

    internal static RunRecord Read(string path) =>
        JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), Format)
        ?? throw new InvalidOperationException($"{path} does not describe a run.");
}
