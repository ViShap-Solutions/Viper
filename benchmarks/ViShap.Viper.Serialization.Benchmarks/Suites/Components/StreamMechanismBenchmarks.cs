using BenchmarkDotNet.Attributes;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-09 — the stream mechanisms of §7 against the bare stream they wrap:
/// <see cref="MeteredWriteStream"/>, <see cref="MeteredReadStream"/> and
/// <see cref="WindowReadStream"/>.
/// </summary>
/// <remarks>
/// One invocation moves 64 KB in 256 calls, and the cell is per call, because what a wrapper adds is a
/// per-call comparison rather than a per-byte cost. The bare rows are the same 256 calls straight to a
/// <see cref="MemoryStream"/>, so the overhead of metering is a difference on this table alone.
/// <para>
/// The metered budget is set to a ceiling a run cannot reach. The check it performs is the one a real
/// budget performs — a single comparison — so what this measures is the cost of metering rather than
/// the cost of a particular ceiling. A window has a fixed length and no rewind, so it is constructed
/// per invocation and the last cell is that construction, amortized over the same 256 calls.
/// </para>
/// <para>
/// Explains DIFF-06, where the stream family is compared with the <c>byte[]</c> family end to end.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class StreamMechanismBenchmarks
{
    private const int Chunk = 256;
    private const int Chunks = 256;
    private const int Total = Chunk * Chunks;

    private readonly byte[] _buffer = new byte[Chunk];

    private MemoryStream _bareDestination = null!;
    private MemoryStream _meteredInner = null!;
    private MeteredWriteStream _metered = null!;

    private MemoryStream _bareSource = null!;
    private MemoryStream _meteredSource = null!;
    private MeteredReadStream _meteredReader = null!;
    private MemoryStream _windowSource = null!;

    [GlobalSetup]
    public void Setup()
    {
        var payload = new byte[Total];

        _bareDestination = new MemoryStream(Total);
        _meteredInner = new MemoryStream(Total);
        _metered = new MeteredWriteStream(_meteredInner, long.MaxValue, "MICRO-09");

        _bareSource = new MemoryStream(payload, writable: false);
        _meteredSource = new MemoryStream(payload, writable: false);
        _meteredReader = new MeteredReadStream(_meteredSource, long.MaxValue, "MICRO-09");
        _windowSource = new MemoryStream(payload, writable: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _bareDestination.Dispose();
        _meteredInner.Dispose();
        _bareSource.Dispose();
        _meteredSource.Dispose();
        _windowSource.Dispose();
    }

    [Benchmark(Description = "MICRO-09 bare write", OperationsPerInvoke = Chunks)]
    public long BareWrite()
    {
        _bareDestination.Position = 0;

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            _bareDestination.Write(_buffer);
        }

        return _bareDestination.Position;
    }

    [Benchmark(Description = "MICRO-09 metered write", OperationsPerInvoke = Chunks)]
    public long MeteredWrite()
    {
        _metered.Position = 0;

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            _metered.Write(_buffer);
        }

        return _metered.Position;
    }

    [Benchmark(Description = "MICRO-09 bare read", OperationsPerInvoke = Chunks)]
    public long BareRead()
    {
        _bareSource.Position = 0;
        long read = 0;

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            read += _bareSource.Read(_buffer);
        }

        return read;
    }

    [Benchmark(Description = "MICRO-09 metered read", OperationsPerInvoke = Chunks)]
    public long MeteredRead()
    {
        _meteredSource.Position = 0;
        long read = 0;

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            read += _meteredReader.Read(_buffer);
        }

        return read;
    }

    [Benchmark(Description = "MICRO-09 window read", OperationsPerInvoke = Chunks)]
    public long WindowRead()
    {
        _windowSource.Position = 0;
        var window = new WindowReadStream(_windowSource, Total, "MICRO-09");
        long read = 0;

        for (var chunk = 0; chunk < Chunks; chunk++)
        {
            read += window.Read(_buffer);
        }

        return read;
    }

    [Benchmark(Description = "MICRO-09 window construction", OperationsPerInvoke = Chunks)]
    public object WindowConstruction() => new WindowReadStream(_windowSource, Total, "MICRO-09");
}
