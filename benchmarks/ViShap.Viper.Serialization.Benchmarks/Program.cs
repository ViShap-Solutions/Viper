using BenchmarkDotNet.Running;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Scenarios;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Compression;

namespace ViShap.Viper.Serialization.Benchmarks;

public static class Program
{
    public static IBenchmarkSerializer CreateAdapter(string name) => name switch
    {
        "Viper" => new ViperAdapter(),
        "System.Text.Json" => new SystemTextJsonAdapter(),
        "System.Text.Json.SourceGen" => new SystemTextJsonSourceGenAdapter(),
        "protobuf-net" => new ProtobufNetAdapter(),
        "MessagePack" => new MessagePackAdapter(),
        "Orleans" => new OrleansAdapter(),
        "ZeroFormatter" => new ZeroFormatterAdapter(),
        "MemoryPack" => new MemoryPackAdapter(),
        "XmlSerializer" => new XmlSerializerAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown benchmark serializer")
    };

    public static IStreamBenchmarkSerializer CreateStreamAdapter(string name) => name switch
    {
        "Viper" => new ViperStreamAdapter(),
        "System.Text.Json" => new SystemTextJsonStreamAdapter(),
        "protobuf-net" => new ProtobufNetStreamAdapter(),
        "MessagePack" => new MessagePackStreamAdapter(),
        "Orleans.Serialization" => new OrleansStreamAdapter(),
        "XmlSerializer" => new XmlSerializerStreamAdapter(),
        _ => throw new NotSupportedException($"No fair stream API configured for {name}")
    };

    public static void Main(string[] args)
    {
        Directory.CreateDirectory("BenchmarkReports");
        File.WriteAllText("BenchmarkReports/environment.md", EnvironmentManifest());
        WritePayloadSizes();
        WriteCompressionMetrics();
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new BenchmarkConfig());
    }

    private static void WritePayloadSizes()
    {
        var libraries = new[] { "Viper", "System.Text.Json", "System.Text.Json.SourceGen", "protobuf-net", "MessagePack", "Orleans", "ZeroFormatter", "MemoryPack", "XmlSerializer" };
        var dataSets = Enum.GetValues<DataSetKind>();
        using var writer = new StreamWriter("BenchmarkReports/payload-sizes.csv");
        writer.WriteLine("Library,DataSet,PayloadBytes");
        foreach (var library in libraries)
        foreach (var dataSet in dataSets)
        {
            try
            {
                var adapter = CreateAdapter(library);
                try
                {
                    var payload = adapter.Serialize(BenchmarkDataSet.Create(dataSet));
                    writer.WriteLine($"\"{library}\",{dataSet},{payload.Length}");
                }
                finally
                {
                    (adapter as IDisposable)?.Dispose();
                }
            }
            catch (Exception ex)
            {
                writer.WriteLine($"\"{library}\",{dataSet},unsupported:{ex.GetType().Name}");
            }
        }
    }

    private static void WriteCompressionMetrics()
    {
        var raw = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("compressible payload ", 4096)));
        using var writer = new StreamWriter("BenchmarkReports/compression-metrics.csv");
        writer.WriteLine("Algorithm,RawBytes,CompressedBytes,CompressionRatio");
        foreach (var item in new[]
        {
            ("None", Compressor.None),
            ("Deflate", new Compressor(new Deflate())),
            ("Brotli", new Compressor(new Brotli()))
        })
        {
            var compressed = item.Item2.Compress(raw);
            var ratio = raw.Length == 0 ? 0d : (double)compressed.Length / raw.Length;
            writer.WriteLine($"{item.Item1},{raw.Length},{compressed.Length},{ratio:R}");
        }
    }

    private static string EnvironmentManifest() => $"""
                                                    # Benchmark environment

                                                    - Date (UTC): {DateTimeOffset.UtcNow:O}
                                                    - OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}
                                                    - Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}
                                                    - Process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}
                                                    - Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}
                                                    - CPU logical processors: {Environment.ProcessorCount}
                                                    - .NET runtime: {Environment.Version}
                                                    - BenchmarkDotNet: 0.15.8
                                                    - protobuf-net: 3.4.21
                                                    - MessagePack: 3.1.8
                                                    - Microsoft.Orleans.Serialization: 10.2.1
                                                    - Microsoft.Extensions.DependencyInjection: 10.0.1
                                                    - ZeroFormatter: 1.6.4
                                                    - MemoryPack: 1.21.4
                                                    - Configuration: Release/net10.0

                                                    Run on a dedicated, otherwise idle machine. Commit this manifest together with the benchmark baseline produced by the same source revision.
                                                    """;
}