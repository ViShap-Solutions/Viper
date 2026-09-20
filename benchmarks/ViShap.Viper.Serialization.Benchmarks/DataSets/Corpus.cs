namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

/// <summary>
/// The corpus of Benchmark-Plan §9. One instance per dataset, built once and shared, so every suite
/// measures the same bytes and no benchmark pays for construction.
/// </summary>
public static class Corpus
{
    /// <summary>Every dataset that exists today, in plan order.</summary>
    internal static IReadOnlyList<Dataset> All { get; } =
    [
        new TinyFlatDataset(),
        new MediumObjectDataset(),
        new RecordBatchSmallDataset(),
        new RecordBatchLargeDataset(),
        new DictionaryHeavyDataset(),
        new DeepGraphDataset(5),
        new DeepGraphDataset(10),
        new DeepGraphDataset(25),
        new DeepGraphDataset(50),
        new DeepGraphDataset(200),
        new DeepGraphDataset(511),
        new UnicodeHeavyDataset(ascii: false),
        new UnicodeHeavyDataset(ascii: true),
        new IncompressibleDataset(),
        new HighlyCompressibleDataset(),
        new SharedReferenceDagDataset(),
        new CyclicGraphDataset(),
        new PolymorphicBatchDataset(),
        new KeyedEvolutionDataset(),
        new ByteBlobDataset(),
        new BlobBatchDataset(),
        new NumericArraysDataset(),
        new StringTableDataset(),
        new NullSparseDataset(),
        new WideObjectDataset(),
        new CollectionZooDataset(),
        new TimeAndNumericsDataset(),
    ];

    /// <summary>The datasets a suite uses when it needs breadth rather than the whole corpus.</summary>
    internal static IReadOnlyList<Dataset> Core { get; } =
    [
        Find("DATA-01"),
        Find("DATA-02"),
        Find("DATA-03"),
        Find("DATA-04"),
        Find("DATA-05"),
        Find("DATA-07/unicode"),
    ];

    public static Dataset Find(string id) =>
        All.FirstOrDefault(dataset => dataset.Id == id)
        ?? throw new ArgumentException($"No dataset with id '{id}'.", nameof(id));
}
