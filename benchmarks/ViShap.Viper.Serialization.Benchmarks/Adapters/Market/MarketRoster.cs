namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// The seven mandatory libraries of Benchmark-Plan §5.2, each in the configurations its tiers need.
/// Every configuration is built once per process and shared, as a consumer shares one serializer.
/// </summary>
internal static class MarketRoster
{
    private static readonly Lazy<IReadOnlyList<MarketAdapter>> Adapters = new(Build);

    internal static IReadOnlyList<MarketAdapter> All => Adapters.Value;

    internal static MarketAdapter Find(string id) =>
        All.FirstOrDefault(adapter => adapter.Id == id || adapter.Name == id)
        ?? throw new ArgumentException($"No market adapter '{id}'.", nameof(id));

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
