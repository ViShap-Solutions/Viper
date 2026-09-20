using BenchmarkDotNet.Attributes;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// §21 — real parallel work over one shared serializer, since the caches are shared and that is how
/// an application uses it. Threads are started per operation batch rather than simulated by a loop.
/// </summary>
[MemoryDiagnoser]
public class ConcurrencyBenchmarks
{
    private const int OperationsPerThread = 200;

    private ViperAdapter _adapter = null!;
    private Dataset _dataset = null!;
    private byte[] _payload = [];

    public static IEnumerable<int> ThreadCounts =>
        [1, 2, 4, 8, System.Environment.ProcessorCount];

    public static IEnumerable<Dataset> Datasets =>
    [
        Corpus.Find("DATA-01"),
        Corpus.Find("DATA-02"),
        Corpus.Find("DATA-03"),
    ];

    [ParamsSource(nameof(ThreadCounts))]
    public int Threads { get; set; }

    [ParamsSource(nameof(Datasets))]
    public Dataset Data { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _adapter = new ViperAdapter(ViperProfile.Default);
        _dataset = Data;
        _payload = _dataset.Serialize(_adapter);

        // The type plan is built once here, so the measurement is steady-state contention rather
        // than first-use construction, which §20 measures on its own.
        _ = _dataset.Deserialize(_adapter, _payload);
    }

    [Benchmark(Description = "PAR serialize on N threads")]
    public void SerializeParallel() => Run(() => _dataset.Serialize(_adapter));

    [Benchmark(Description = "PAR deserialize on N threads")]
    public void DeserializeParallel() => Run(() => _dataset.Deserialize(_adapter, _payload));

    private void Run(Func<object?> operation)
    {
        var tasks = new Task[Threads];

        for (var thread = 0; thread < Threads; thread++)
        {
            tasks[thread] = Task.Factory.StartNew(
                () =>
                {
                    for (var i = 0; i < OperationsPerThread; i++)
                    {
                        _ = operation();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        Task.WaitAll(tasks);
    }
}
