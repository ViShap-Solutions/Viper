using BenchmarkDotNet.Attributes;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Io;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-08 — the V1 header of service records: writing it, and parsing it back with every record
/// and every declared length verified. Its bytes are the associated data authenticated encryption
/// binds, so there is no separate image to build.
/// </summary>
/// <remarks>
/// Measured twice: as the header a default configuration produces, with no service record, and as a
/// wide one — all three services, a custom name for each algorithm family, a key id and a checksum. The pair
/// is what DIFF-01 subtracts, so a V1-minus-V0 differential that exceeds it by more than its margin is
/// buffering or framing rather than the header.
/// </remarks>
[MemoryDiagnoser]
public class HeaderBenchmarks
{
    private OperationState _operation;
    private BinaryFormatHeaderV1 _header;
    private PayloadBuffer _destination = null!;
    private byte[] _encoded = [];

    /// <summary>
    /// <c>false</c> is the header a default write produces; <c>true</c> carries all three service
    /// records, with a custom name for each algorithm family, a key id and a checksum.
    /// </summary>
    [Params(false, true)]
    public bool AllFields { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.Operation();

        _header = AllFields
            ? new BinaryFormatHeaderV1(
                PreserveReferences: true,
                ChecksumAlgorithm.Custom, "benchmark-checksum", [0x11, 0x22, 0x33, 0x44],
                CompressionAlgorithm.Custom, "benchmark-compression", UncompressedLength: 4_096,
                EncryptionAlgorithm.Custom, "benchmark-encryption", "benchmark-key-2026-09",
                OnDiskLength: 2_076)
            : new BinaryFormatHeaderV1(
                PreserveReferences: false,
                ChecksumAlgorithm.None, null, [],
                CompressionAlgorithm.None, null, UncompressedLength: 0,
                EncryptionAlgorithm.None, null, null,
                OnDiskLength: 4_096);

        _destination = new PayloadBuffer(_operation.Limits.MaxWireBytes, "wire");
        _encoded = ComponentFixtures.Encode(ref _operation, _header.WriteTo);
    }

    [GlobalCleanup]
    public void Cleanup() => _destination.Dispose();

    [Benchmark(Description = "MICRO-08 write header")]
    public long Write()
    {
        var writer = new WireWriter(_destination, ref _operation);
        _header.WriteTo(ref writer);
        writer.Flush();

        long written = _destination.Length;
        _destination.Dispose();
        return written;
    }

    [Benchmark(Description = "MICRO-08 parse header")]
    public int Parse()
    {
        var reader = new WireReader(_encoded, ref _operation);
        return BinaryFormatHeaderV1.ReadFrom(ref reader).OnDiskLength;
    }
}
