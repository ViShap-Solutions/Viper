using System.Collections;
using System.Collections.Concurrent;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>The capability tiers of Benchmark-Plan §6.</summary>
[Flags]
internal enum Tier
{
    None = 0,
    T0 = 1,
    T1 = 2,
    T2 = 4,
    T3 = 8,
    T4 = 16,
}

/// <summary>
/// The corpus of §9 as each library's models carry it: every dataset projected once into a family's
/// namesake types, keeping its id, its purpose and what it asks of identity.
/// </summary>
internal static class MarketCorpus
{
    private static readonly ConcurrentDictionary<ModelFamily, IReadOnlyList<Dataset>> Families = new();

    /// <summary>Every dataset of the corpus in <paramref name="family"/>'s models, in plan order.</summary>
    internal static IReadOnlyList<Dataset> For(ModelFamily family) =>
        family == ModelFamily.Viper ? Corpus.All : Families.GetOrAdd(family, static f => [.. Corpus.All.Select(dataset => Project(dataset, f))]);

    internal static Dataset Find(ModelFamily family, string id) =>
        For(family).FirstOrDefault(dataset => dataset.Id == id)
        ?? throw new ArgumentException($"No dataset with id '{id}'.", nameof(id));

    /// <summary>The tiers a dataset belongs to, as the table of §9 states them.</summary>
    internal static Tier TiersOf(string id) => id.Split('/')[0] switch
    {
        "DATA-01" or "DATA-02" or "DATA-03" or "DATA-06" => Tier.T0 | Tier.T1 | Tier.T2 | Tier.T3,
        "DATA-04" or "DATA-05" or "DATA-07" or "DATA-15" or "DATA-16" or "DATA-17" or "DATA-19" or "DATA-20" => Tier.T0 | Tier.T1 | Tier.T2,
        "DATA-08" or "DATA-09" => Tier.T0 | Tier.T4,
        "DATA-10" or "DATA-11" => Tier.T3,
        "DATA-12" => Tier.T2 | Tier.T3,
        "DATA-13" => Tier.T2,
        "DATA-14" => Tier.T0 | Tier.T1 | Tier.T2 | Tier.T4,
        "DATA-18" => Tier.T0 | Tier.T2,
        _ => throw new ArgumentException($"Dataset '{id}' is not in the table of §9.", nameof(id)),
    };

    private static Dataset Project(Dataset source, ModelFamily family)
    {
        var type = ModelFamilies.Map(source.ValueType, family);
        var value = Projection.Project(source.BoxedValue, family);

        return (Dataset)Activator.CreateInstance(typeof(ProjectedDataset<>).MakeGenericType(type), source, value)!;
    }
}

/// <summary>A dataset of §9 carried into another library's models by <see cref="Projection"/>.</summary>
internal sealed class ProjectedDataset<T>(Dataset source, object? value) : Dataset<T>
{
    internal Dataset Source { get; } = source;

    internal override string Id => Source.Id;

    internal override string Name => Source.Name;

    internal override string Purpose => Source.Purpose;

    internal override bool RequiresReferences => Source.RequiresReferences;

    internal override bool IdentitySensitive => Source.IdentitySensitive;

    internal override string? VerifyIdentity(object restored) => Source.VerifyIdentity(restored);

    protected override T Build() => (T)value!;
}

/// <summary>
/// Reaches into a graph by member name, so an identity check written once holds for every family's
/// models alike.
/// </summary>
internal static class Graph
{
    internal static object? Member(object? owner, string name) =>
        owner?.GetType().GetProperty(name) is { } property
            ? property.GetValue(owner)
            : throw new InvalidOperationException($"No member '{name}' on {owner?.GetType().Name ?? "null"}.");

    /// <summary>The <paramref name="index"/>th of a node's <c>Children</c>.</summary>
    internal static object? Child(object? node, int index) =>
        Member(node, "Children") is IList children && index < children.Count
            ? children[index]
            : throw new InvalidOperationException($"No child {index} on {node?.GetType().Name ?? "null"}.");
}
