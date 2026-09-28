using BenchmarkDotNet.Attributes;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Io;
using ViShap.Viper.Metadata;
using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-08 — the V1 header: writing it, parsing it back with every declared length verified, and
/// building the canonical image that authenticated encryption binds as associated data.
/// </summary>
/// <remarks>
/// Measured twice: as the header a default configuration produces, and as the widest one the format
/// admits — custom names for all three algorithm families, a key id, and a checksum present. The pair
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
    /// <c>false</c> is the header a default write produces; <c>true</c> carries a custom name for each
    /// algorithm family, a key id and a checksum, which is every optional field the format has.
    /// </summary>
    [Params(false, true)]
    public bool AllFields { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _operation = ComponentFixtures.Operation();

        _header = AllFields
            ? new BinaryFormatHeaderV1(
                CompressionAlgorithm.Custom, "benchmark-compression",
                ChecksumAlgorithm.Custom, "benchmark-checksum",
                EncryptionAlgorithm.Custom, "benchmark-encryption",
                "benchmark-key-2026-09", PreserveReferences: true,
                UncompressedLength: 4_096, CompressedLength: 2_048, OnDiskLength: 2_076,
                Checksum: [0x11, 0x22, 0x33, 0x44])
            : new BinaryFormatHeaderV1(
                CompressionAlgorithm.None, null,
                ChecksumAlgorithm.None, null,
                EncryptionAlgorithm.None, null,
                null, PreserveReferences: false,
                UncompressedLength: 4_096, CompressedLength: 4_096, OnDiskLength: 4_096,
                Checksum: []);

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
        return BinaryFormatHeaderV1.ReadFrom(ref reader).UncompressedLength;
    }

    [Benchmark(Description = "MICRO-08 build associated data")]
    public byte[] BuildAssociatedData() => _header.BuildAssociatedData();
}
