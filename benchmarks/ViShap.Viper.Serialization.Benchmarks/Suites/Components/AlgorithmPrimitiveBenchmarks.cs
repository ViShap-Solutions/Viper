using BenchmarkDotNet.Attributes;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-10 — the compression primitives over spans, outside the pipeline: <see cref="Deflate"/> and
/// <see cref="Brotli"/>, in both directions, at three sizes and two entropies.
/// </summary>
/// <remarks>
/// Buffers are sized once from <c>GetMaxCompressedLength</c>, which is what the pipeline does inside
/// the phase barrier, so a cell is the codec and not an allocation. Explains the compression half of
/// DIFF-02 and every CMP row of §15: a B-P3 write is this cell plus the payload the phase before it
/// produced.
/// </remarks>
[MemoryDiagnoser]
public class CompressionPrimitiveBenchmarks
{
    private readonly Deflate _deflate = new();
    private readonly Brotli _brotli = new();

    private byte[] _source = [];
    private byte[] _compressBuffer = [];
    private byte[] _deflated = [];
    private byte[] _brotlied = [];
    private byte[] _decompressBuffer = [];

    [Params(4_096, 65_536, 1_000_000)]
    public int Bytes { get; set; }

    /// <summary>
    /// <c>true</c> is text-like data a codec can shrink; <c>false</c> is random bytes it cannot, which
    /// is the worst case the pipeline still has to pay for.
    /// </summary>
    [Params(false, true)]
    public bool Compressible { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new DeterministicRandom(0x0000_1810UL + (ulong)Bytes);

        _source = new byte[Bytes];

        if (Compressible)
        {
            var pattern = "the quick brown fox jumps over the lazy dog "u8;

            for (var index = 0; index < _source.Length; index++)
            {
                _source[index] = pattern[index % pattern.Length];
            }
        }
        else
        {
            random.NextBytes(_source);
        }

        _compressBuffer = new byte[Math.Max(
            _deflate.GetMaxCompressedLength(Bytes), _brotli.GetMaxCompressedLength(Bytes))];

        _deflated = Compress(_deflate);
        _brotlied = Compress(_brotli);
        _decompressBuffer = new byte[Bytes];
    }

    [Benchmark(Description = "MICRO-10 deflate compress")]
    public int DeflateCompress() => _deflate.Compress(_source, _compressBuffer);

    [Benchmark(Description = "MICRO-10 deflate decompress")]
    public int DeflateDecompress() => _deflate.Decompress(_deflated, _decompressBuffer);

    [Benchmark(Description = "MICRO-10 brotli compress")]
    public int BrotliCompress() => _brotli.Compress(_source, _compressBuffer);

    [Benchmark(Description = "MICRO-10 brotli decompress")]
    public int BrotliDecompress() => _brotli.Decompress(_brotlied, _decompressBuffer);

    private byte[] Compress(ICompressionAlgorithm algorithm)
    {
        var buffer = new byte[algorithm.GetMaxCompressedLength(Bytes)];
        int written = algorithm.Compress(_source, buffer);
        return buffer.AsSpan(0, written).ToArray();
    }
}

/// <summary>
/// MICRO-10 — the checksum and cipher primitives over spans: <see cref="Crc32"/> and
/// <see cref="Aes256Gcm"/>, at three sizes.
/// </summary>
/// <remarks>
/// Entropy does not change what either costs, so size is the only parameter. The cipher is measured
/// with associated data, because that is how the pipeline calls it: the V1 header is bound as AAD, and
/// a cell taken without it would understate an encrypted write. Explains the checksum and encryption
/// halves of DIFF-02 and the §16 rows.
/// </remarks>
[MemoryDiagnoser]
public class ProtectionPrimitiveBenchmarks
{
    private static readonly byte[] Key =
    [
        0x1F, 0x2E, 0x3D, 0x4C, 0x5B, 0x6A, 0x79, 0x88,
        0x97, 0xA6, 0xB5, 0xC4, 0xD3, 0xE2, 0xF1, 0x00,
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
        0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x01,
    ];

    private readonly Crc32 _crc32 = new();
    private readonly Aes256Gcm _cipher = new();

    private byte[] _source = [];
    private byte[] _checksum = [];
    private byte[] _associatedData = [];
    private byte[] _ciphertext = [];
    private byte[] _cipherBuffer = [];
    private byte[] _plainBuffer = [];

    [Params(4_096, 65_536, 1_000_000)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = new DeterministicRandom(0x0000_1811UL + (ulong)Bytes).NextBytes(Bytes);
        _checksum = new byte[_crc32.HashSizeInBytes];
        _associatedData = new byte[64];

        _cipherBuffer = new byte[_cipher.GetMaxCiphertextLength(Bytes)];
        int written = _cipher.Encrypt(_source, Key, _associatedData, _cipherBuffer);
        _ciphertext = _cipherBuffer.AsSpan(0, written).ToArray();
        _plainBuffer = new byte[Bytes];
    }

    [Benchmark(Description = "MICRO-10 crc32 compute")]
    public byte[] Crc32Compute()
    {
        _crc32.Compute(_source, _checksum);
        return _checksum;
    }

    [Benchmark(Description = "MICRO-10 aes-256-gcm encrypt")]
    public int Encrypt() => _cipher.Encrypt(_source, Key, _associatedData, _cipherBuffer);

    [Benchmark(Description = "MICRO-10 aes-256-gcm decrypt")]
    public int Decrypt() => _cipher.Decrypt(_ciphertext, Key, _associatedData, _plainBuffer);
}
