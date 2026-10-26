using System.Buffers;
using BenchmarkDotNet.Attributes;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Suites.Components;

/// <summary>
/// MICRO-10 — the compression primitives outside the pipeline: <see cref="DeflateCompression"/> and
/// <see cref="BrotliCompression"/>, in both directions, at three sizes and two entropies.
/// </summary>
/// <remarks>
/// Each direction writes into one buffer writer that is reset between invocations and has already
/// grown past the largest output, so a cell is the codec and not an allocation. Explains the compression half of
/// DIFF-02 and every CMP row of §15: a B-P3 write is this cell plus the payload the phase before it
/// produced.
/// </remarks>
[MemoryDiagnoser]
public class CompressionPrimitiveBenchmarks
{
    private readonly DeflateCompression _deflate = new();
    private readonly BrotliCompression _brotli = new();
    private readonly ArrayBufferWriter<byte> _output = new();

    private byte[] _source = [];
    private byte[] _deflated = [];
    private byte[] _brotlied = [];

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

        _deflated = Compress(_deflate);
        _brotlied = Compress(_brotli);

        _output.GetSpan(Math.Max(Bytes, Math.Max(_deflated.Length, _brotlied.Length)) * 2);
        _output.ResetWrittenCount();
    }

    [Benchmark(Description = "MICRO-10 deflate compress")]
    public int DeflateCompress()
    {
        _output.ResetWrittenCount();
        _deflate.Compress(_source, _output);
        return _output.WrittenCount;
    }

    [Benchmark(Description = "MICRO-10 deflate decompress")]
    public int DeflateDecompress()
    {
        _output.ResetWrittenCount();
        _deflate.Decompress(_deflated, _output, Bytes);
        return _output.WrittenCount;
    }

    [Benchmark(Description = "MICRO-10 brotli compress")]
    public int BrotliCompress()
    {
        _output.ResetWrittenCount();
        _brotli.Compress(_source, _output);
        return _output.WrittenCount;
    }

    [Benchmark(Description = "MICRO-10 brotli decompress")]
    public int BrotliDecompress()
    {
        _output.ResetWrittenCount();
        _brotli.Decompress(_brotlied, _output, Bytes);
        return _output.WrittenCount;
    }

    private byte[] Compress(ICompressionAlgorithm algorithm)
    {
        var buffer = new ArrayBufferWriter<byte>();
        algorithm.Compress(_source, buffer);
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
/// MICRO-10 — the checksum and cipher primitives over spans: <see cref="Crc32Checksum"/>,
/// <see cref="XxHash3Checksum"/>, <see cref="XxHash128Checksum"/>, <see cref="Aes256GcmEncryption"/> and
/// <see cref="ChaCha20Poly1305Encryption"/>, at three sizes.
/// </summary>
/// <remarks>
/// Entropy does not change what any of them costs, so size is the only parameter. The ciphers are
/// measured with associated data, because that is how the pipeline calls them: the V1 header is bound
/// as AAD, and a cell taken without it would understate an encrypted write. ChaCha20-Poly1305 against
/// AES-GCM depends on whether the processor has AES instructions, which the manifest's CPU names.
/// Explains the checksum and encryption halves of DIFF-02 and the §16 rows.
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

    private readonly Crc32Checksum _crc32 = new();
    private readonly XxHash3Checksum _xxHash3 = new();
    private readonly XxHash128Checksum _xxHash128 = new();
    private readonly Aes256GcmEncryption _cipher = new();
    private readonly ChaCha20Poly1305Encryption _chaCha = new();

    private byte[] _source = [];
    private byte[] _crc32Digest = [];
    private byte[] _xxHash3Digest = [];
    private byte[] _xxHash128Digest = [];
    private byte[] _associatedData = [];
    private byte[] _ciphertext = [];
    private byte[] _chaChaCiphertext = [];
    private byte[] _cipherBuffer = [];
    private byte[] _plainBuffer = [];

    [Params(4_096, 65_536, 1_000_000)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _source = new DeterministicRandom(0x0000_1811UL + (ulong)Bytes).NextBytes(Bytes);
        _crc32Digest = new byte[_crc32.HashSizeInBytes];
        _xxHash3Digest = new byte[_xxHash3.HashSizeInBytes];
        _xxHash128Digest = new byte[_xxHash128.HashSizeInBytes];
        _associatedData = new byte[64];

        _cipherBuffer = new byte[_cipher.GetCiphertextLength(Bytes)];
        _cipher.Encrypt(_source, Key, _associatedData, _cipherBuffer);
        _ciphertext = [.. _cipherBuffer];

        _chaCha.Encrypt(_source, Key, _associatedData, _cipherBuffer);
        _chaChaCiphertext = [.. _cipherBuffer];

        _plainBuffer = new byte[_cipherBuffer.Length];
    }

    [Benchmark(Description = "MICRO-10 crc32 compute")]
    public byte[] Crc32Compute()
    {
        _crc32.Compute(_source, _crc32Digest);
        return _crc32Digest;
    }

    [Benchmark(Description = "MICRO-10 xxhash3 compute")]
    public byte[] XxHash3Compute()
    {
        _xxHash3.Compute(_source, _xxHash3Digest);
        return _xxHash3Digest;
    }

    [Benchmark(Description = "MICRO-10 xxhash128 compute")]
    public byte[] XxHash128Compute()
    {
        _xxHash128.Compute(_source, _xxHash128Digest);
        return _xxHash128Digest;
    }

    [Benchmark(Description = "MICRO-10 aes-256-gcm encrypt")]
    public int Encrypt() => _cipher.Encrypt(_source, Key, _associatedData, _cipherBuffer);

    [Benchmark(Description = "MICRO-10 aes-256-gcm decrypt")]
    public int Decrypt() => _cipher.Decrypt(_ciphertext, Key, _associatedData, _plainBuffer);

    [Benchmark(Description = "MICRO-10 chacha20-poly1305 encrypt")]
    public int ChaChaEncrypt() => _chaCha.Encrypt(_source, Key, _associatedData, _cipherBuffer);

    [Benchmark(Description = "MICRO-10 chacha20-poly1305 decrypt")]
    public int ChaChaDecrypt() => _chaCha.Decrypt(_chaChaCiphertext, Key, _associatedData, _plainBuffer);
}
