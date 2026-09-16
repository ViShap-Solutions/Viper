using BenchmarkDotNet.Attributes;
using System.Security.Cryptography;
using ViShap.Viper;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.Scenarios;

[MemoryDiagnoser]
[BenchmarkCategory("Compression")]
public class CompressionBenchmarks
{
    [Params(CompressionAlgorithmKind.None,CompressionAlgorithmKind.Deflate,CompressionAlgorithmKind.Brotli)] public CompressionAlgorithmKind Algorithm {get;set;}
    private byte[] _raw=null!; private byte[] _compressed=null!; private Compressor _compressor=null!;
    [GlobalSetup] public void Setup(){_raw=System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("compressible payload ",4096)));_compressor=Algorithm switch {CompressionAlgorithmKind.Deflate=>new Compressor(new Deflate()),CompressionAlgorithmKind.Brotli=>new Compressor(new Brotli()),_=>Compressor.None};_compressed=_compressor.Compress(_raw);}
    [Benchmark] public byte[] Compress()=>_compressor.Compress(_raw);
    [Benchmark] public byte[] Decompress()=>_compressor.Decompress(_compressor.DefaultKind,_compressor.DefaultCustomName,_compressed,_raw.Length);
    public enum CompressionAlgorithmKind{None,Deflate,Brotli}
}

[MemoryDiagnoser]
[BenchmarkCategory("Encryption")]
public class EncryptionBenchmarks
{
    private byte[] _raw=null!; private byte[] _ciphertext=null!; private readonly byte[] _key=new byte[32]; private Encryptor _encryptor=null!;
    [GlobalSetup] public void Setup(){RandomNumberGenerator.Fill(_key);_raw=new byte[64*1024];RandomNumberGenerator.Fill(_raw);_encryptor=new Encryptor(new Aes256Gcm(),_key);_ciphertext=_encryptor.Encrypt(_raw);}
    [Benchmark] public byte[] Encrypt()=>_encryptor.Encrypt(_raw);
    [Benchmark] public byte[] Decrypt()=>_encryptor.Decrypt(EncryptionAlgorithm.Aes256Gcm,null,_ciphertext,_raw.Length);
}

[MemoryDiagnoser]
[BenchmarkCategory("ReferencePreservation","Viper")]
public class ReferencePreservationBenchmarks
{
    private BenchmarkPayload _value=null!; private BinarySerializer _serializer=null!;
    [GlobalSetup] public void Setup(){_value=BenchmarkDataSet.Create(DataSetKind.CollectionMedium);_serializer=new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build());}
    [Benchmark] public byte[] Serialize()=>_serializer.Serialize(_value);
    [Benchmark] public BenchmarkPayload RoundTrip(){var b=_serializer.Serialize(_value);return _serializer.Deserialize<BenchmarkPayload>(b)!;}
}
