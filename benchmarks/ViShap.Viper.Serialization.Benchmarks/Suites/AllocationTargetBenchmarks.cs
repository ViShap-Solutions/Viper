using System.Buffers;
using BenchmarkDotNet.Attributes;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;

namespace ViShap.Viper.Serialization.Benchmarks.Suites;

/// <summary>
/// ALLOC-10 to ALLOC-20 — the allocation targets of the rework's §11, one cell per line of its table,
/// each with the reference cell it is read against. A target is a number of bytes per operation after
/// warm-up; the reference cells allocate exactly the objects a target allows — the returned record, the
/// returned graph, the returned array — so a target is met when its cell equals its reference, and the
/// difference is what the path adds.
/// </summary>
/// <remarks>
/// ALLOC-21, the first use of a type, is not a steady-state cell: it is the construction the contract
/// cold runner records, and every cell here is taken after that first use.
/// <para>
/// The values are prepared in <see cref="Setup"/>; a buffer writer is reset rather than reallocated, and
/// a stream is rewound, so its growth belongs to no cell.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class AllocationTargetBenchmarks
{
    private const int GraphSize = 256;

    private static readonly byte[] Key =
    [
        0x60, 0x3d, 0xeb, 0x10, 0x15, 0xca, 0x71, 0xbe, 0x2b, 0x73, 0xae, 0xf0, 0x85, 0x7d, 0x77, 0x81,
        0x1f, 0x35, 0x2c, 0x07, 0x3b, 0x61, 0x08, 0xd7, 0x2d, 0x98, 0x10, 0xa3, 0x09, 0x14, 0xdf, 0xf4
    ];

    private readonly BinarySerializer _v1 = new();

    private readonly BinarySerializer _v0 =
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    private readonly BinarySerializer _references =
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    private readonly BinarySerializer _brotli =
        new(BinarySerializerOptions.Configure().WithCompression(new BrotliCompression()).Build());

    private readonly BinarySerializer _deflate =
        new(BinarySerializerOptions.Configure().WithCompression(new DeflateCompression()).Build());

    private readonly BinarySerializer _encrypted =
        new(BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), Key, "bench").Build());

    private readonly ArrayBufferWriter<byte> _destination = new(1 << 20);
    private readonly MemoryStream _stream = new(new byte[1 << 20], 0, 1 << 20, writable: true, publiclyVisible: true);

    private MixedRecord _record = null!;
    private List<PrimitiveRecord> _graph = [];
    private byte[] _recordPayload = [];
    private byte[] _graphPayload = [];
    private byte[] _referencePayload = [];
    private MemoryStream _source = null!;
    private int _frameLength;

    [GlobalSetup]
    public void Setup()
    {
        _record = MixedRecord.Sample();
        _graph = [.. Enumerable.Range(0, GraphSize).Select(PrimitiveRecord.Sample)];
        _recordPayload = _v1.Serialize(PrimitiveRecord.Sample(7));
        _graphPayload = _v1.Serialize(_graph);
        _referencePayload = _references.Serialize(_graph);
        _source = new MemoryStream(_graphPayload);
        _frameLength = _v1.Serialize(_record).Length;
    }

    // --- writing: ALLOC-10 to ALLOC-16 ------------------------------------------------------------

    [Benchmark(Description = "ALLOC-10 write V1 → IBufferWriter")]
    public int WriteV1() => Write(_v1, _record);

    [Benchmark(Description = "ALLOC-11 write V0 → IBufferWriter")]
    public int WriteV0() => Write(_v0, _record);

    [Benchmark(Description = "ALLOC-12 write Brotli → IBufferWriter")]
    public int WriteBrotli() => Write(_brotli, _record);

    [Benchmark(Description = "ALLOC-13 write Deflate → IBufferWriter")]
    public int WriteDeflate() => Write(_deflate, _record);

    [Benchmark(Description = "ALLOC-14 write AES-256-GCM → IBufferWriter")]
    public int WriteEncrypted() => Write(_encrypted, _record);

    [Benchmark(Description = "ALLOC-15 Serialize → byte[]")]
    public byte[] SerializeArray() => _v1.Serialize(_record);

    [Benchmark(Description = "ALLOC-15 reference: the array alone")]
    public byte[] ArrayAlone() => new byte[_frameLength];

    [Benchmark(Description = "ALLOC-16 SerializePooled")]
    public int SerializePooled()
    {
        using var payload = _v1.SerializePooled(_record);
        return payload.Span.Length;
    }

    // --- reading: ALLOC-17 and ALLOC-18 -----------------------------------------------------------

    [Benchmark(Description = "ALLOC-17 read record of primitives ← span")]
    public PrimitiveRecord? ReadRecord() => _v1.Deserialize<PrimitiveRecord>(_recordPayload);

    [Benchmark(Description = "ALLOC-17 reference: the record alone")]
    public PrimitiveRecord RecordAlone() => PrimitiveRecord.Sample(7);

    [Benchmark(Description = "ALLOC-18 read graph ← span")]
    public List<PrimitiveRecord>? ReadGraph() => _v1.Deserialize<List<PrimitiveRecord>>(_graphPayload);

    [Benchmark(Description = "ALLOC-18 write graph → IBufferWriter")]
    public int WriteGraph() => Write(_v1, _graph);

    [Benchmark(Description = "ALLOC-18 reference: the graph alone")]
    public List<PrimitiveRecord> GraphAlone()
    {
        var graph = new List<PrimitiveRecord>(GraphSize);
        for (var index = 0; index < GraphSize; index++)
        {
            graph.Add(PrimitiveRecord.Sample(index));
        }

        return graph;
    }

    // --- ALLOC-19: reference framing ------------------------------------------------------------

    [Benchmark(Description = "ALLOC-19 read graph with PreserveReferences ← span")]
    public List<PrimitiveRecord>? ReadGraphWithReferences() =>
        _references.Deserialize<List<PrimitiveRecord>>(_referencePayload);

    [Benchmark(Description = "ALLOC-19 write graph with PreserveReferences → IBufferWriter")]
    public int WriteGraphWithReferences() => Write(_references, _graph);

    // --- ALLOC-20: the asynchronous methods -------------------------------------------------------

    [Benchmark(Description = "ALLOC-20 SerializeAsync → Stream")]
    public long SerializeAsync()
    {
        _stream.Position = 0;
        _v1.SerializeAsync(_stream, _graph).GetAwaiter().GetResult();
        return _stream.Position;
    }

    [Benchmark(Description = "ALLOC-20 reference: Serialize → Stream")]
    public long SerializeStream()
    {
        _stream.Position = 0;
        _v1.Serialize(_stream, _graph);
        return _stream.Position;
    }

    [Benchmark(Description = "ALLOC-20 DeserializeAsync ← Stream")]
    public List<PrimitiveRecord>? DeserializeAsync()
    {
        _source.Position = 0;
        return _v1.DeserializeAsync<List<PrimitiveRecord>>(_source).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "ALLOC-20 reference: Deserialize ← Stream")]
    public List<PrimitiveRecord>? DeserializeStream()
    {
        _source.Position = 0;
        return _v1.Deserialize<List<PrimitiveRecord>>(_source);
    }

    private int Write<T>(BinarySerializer serializer, T value)
    {
        _destination.ResetWrittenCount();
        serializer.Serialize(_destination, value);
        return _destination.WrittenCount;
    }
}

/// <summary>A record of primitives and strings: the value the write targets of §11 name.</summary>
public sealed class MixedRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Score { get; set; }

    public DayOfWeek Day { get; set; }

    public string? Note { get; set; }

    public long Stamp { get; set; }

    public bool Active { get; set; }

    public static MixedRecord Sample() => new()
    {
        Id = 42,
        Name = "Ada Lovelace",
        Score = 0.75,
        Day = DayOfWeek.Friday,
        Note = "analytical engine",
        Stamp = 1_700_000_000,
        Active = true,
    };
}

/// <summary>A record of primitives: reading one allocates only the record.</summary>
public sealed class PrimitiveRecord
{
    private static readonly Guid SampleKey = new("8B7C0C5A-3E4D-4F2B-9A1E-6D5C4B3A2918");

    public int Id { get; set; }

    public double Score { get; set; }

    public DayOfWeek Day { get; set; }

    public long Stamp { get; set; }

    public bool Active { get; set; }

    public decimal Amount { get; set; }

    public Guid Key { get; set; }

    public static PrimitiveRecord Sample(int index) => new()
    {
        Id = index,
        Score = index / 4.0,
        Day = (DayOfWeek)(index % 7),
        Stamp = 1_700_000_000L + index,
        Active = index % 2 == 0,
        Amount = index * 1.25m,
        Key = SampleKey,
    };
}
