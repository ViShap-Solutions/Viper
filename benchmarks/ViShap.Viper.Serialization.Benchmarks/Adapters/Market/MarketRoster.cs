namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// The seven mandatory libraries of Benchmark-Plan §5.2, each in the configurations its tiers need.
/// Every configuration is built once per process and shared, as a consumer shares one serializer.
/// </summary>
internal static class MarketRoster
{
    private static readonly Lazy<IReadOnlyList<MarketAdapter>> Adapters = new(Build);

    internal static IReadOnlyList<MarketAdapter> All => Adapters.Value;

    private static readonly Lazy<IReadOnlyList<MarketAdapter>> ProtectedAdapters = new(BuildProtected);

    /// <summary>
    /// T4, kept apart from <see cref="All"/>: no library has a protected envelope, so Viper's protected
    /// configurations are compared with the composed baselines of §17 over the three libraries it names,
    /// never probed, never placed in T0–T3, and never timed by the suites of B2.
    /// </summary>
    internal static IReadOnlyList<MarketAdapter> Protected => ProtectedAdapters.Value;

    internal static MarketAdapter Find(string id) =>
        All.Concat(Protected).FirstOrDefault(adapter => adapter.Id == id || adapter.Name == id)
        ?? throw new ArgumentException($"No market adapter '{id}'.", nameof(id));

    private static IReadOnlyList<MarketAdapter> BuildProtected() =>
    [
        ViperMarketAdapter.Protected(),
        ViperMarketAdapter.Sealed(),
        .. ComposedAdapter.Over(
        [
            All.OfType<MemoryPackAdapter>().Single(),
            All.OfType<MessagePackAdapter>().Single(adapter => !adapter.Compressed),
            All.OfType<ProtobufNetAdapter>().Single(),
        ]),
    ];

    private static IReadOnlyList<MarketAdapter> Build() =>
    [
        ViperMarketAdapter.Compact(),
        ViperMarketAdapter.Framed(),
        ViperMarketAdapter.References(),
        ViperMarketAdapter.Deflate(),
        new MemoryPackAdapter(),
        MessagePackAdapter.Default(),
        MessagePackAdapter.Lz4(),
        new ProtobufNetAdapter(),
        NerdbankAdapter.Compact(),
        NerdbankAdapter.Default(),
        NerdbankAdapter.References(),
        new OrleansAdapter(),
        SystemTextJsonAdapter.Default(),
        SystemTextJsonAdapter.Preserve(),
    ];
}
