namespace ViShap.Viper.Serialization.Tests.Algorithms;

[CollectionDefinition("GlobalRegistries", DisableParallelization = true)]
public sealed class GlobalRegistriesCollection : ICollectionFixture<object> { }

[Collection("GlobalRegistries")]
public sealed class RegistryTests
{
    [Fact] public void REG01_BuiltInsResolveToExpectedImplementations()
    {
        Assert.IsType<NoCompression>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.None,null));
        Assert.IsType<Deflate>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.Deflate,null));
        Assert.IsType<Brotli>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.Brotli,null));
        Assert.IsType<NoChecksum>(ChecksumAlgorithmRegistry.Resolve(ChecksumAlgorithm.None,null));
        Assert.IsType<Crc32>(ChecksumAlgorithmRegistry.Resolve(ChecksumAlgorithm.Crc32,null));
        Assert.IsType<NoEncryption>(EncryptionAlgorithmRegistry.Resolve(EncryptionAlgorithm.None,null));
        Assert.IsType<Aes256Gcm>(EncryptionAlgorithmRegistry.Resolve(EncryptionAlgorithm.Aes256Gcm,null));
    }
    [Fact] public void REG02_BuiltInOverrideIsUsedAndRestored()
    {
        var before=CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.None,null); try { CompressionAlgorithmRegistry.Register(CompressionAlgorithm.None,()=>new MarkerCompression()); Assert.IsType<MarkerCompression>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.None,null)); } finally { CompressionAlgorithmRegistry.Register(CompressionAlgorithm.None,()=>new NoCompression()); }
        Assert.IsType<NoCompression>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.None,null));
    }
    [Fact] public void REG03_CustomRegistrationWorksForAllCategories()
    {
        CompressionAlgorithmRegistry.RegisterCustom("qa-compression",()=>new CustomCompression()); ChecksumAlgorithmRegistry.RegisterCustom("qa-checksum-reg",()=>new TestChecksum("qa-checksum-reg")); EncryptionAlgorithmRegistry.RegisterCustom("qa-encryption",()=>new CustomEncryption());
        var key=new byte[32]; var options=BinarySerializerOptions.Configure().WithCompression(new CustomCompression()).WithChecksum(new TestChecksum("qa-checksum-reg")).WithEncryption(new CustomEncryption(),key,"qa-key").Build(); var s=new BinarySerializer(options); var b=s.Serialize(42); using var stream=new MemoryStream(b); var info=BinaryFormatInspector.Peek(stream); Assert.Equal(CompressionAlgorithm.Custom,info!.Value.Compression); Assert.Equal("qa-compression",info.Value.CustomCompressionName); Assert.Equal(ChecksumAlgorithm.Custom,info.Value.ChecksumAlgorithm); Assert.Equal(EncryptionAlgorithm.Custom,info.Value.Encryption); Assert.Equal(42,s.Deserialize<int>(b));
    }
    [Fact] public void REG04_MissingCustomRegistrationIsNotSupported() { Assert.Throws<BinaryFormatNotSupportedException>(() => { CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.Custom,"missing"); }); Assert.Throws<BinaryFormatNotSupportedException>(() => { ChecksumAlgorithmRegistry.Resolve(ChecksumAlgorithm.Custom,"missing"); }); Assert.Throws<BinaryFormatNotSupportedException>(() => { EncryptionAlgorithmRegistry.Resolve(EncryptionAlgorithm.Custom,"missing"); }); }
    [Fact] public void REG05_NullFactoriesAreRejected() { Assert.Throws<ArgumentNullException>(() => { CompressionAlgorithmRegistry.Register(CompressionAlgorithm.None,null!); }); Assert.Throws<ArgumentNullException>(() => { CompressionAlgorithmRegistry.RegisterCustom("x",null!); }); Assert.Throws<ArgumentNullException>(() => { ChecksumAlgorithmRegistry.Register(ChecksumAlgorithm.None,null!); }); Assert.Throws<ArgumentNullException>(() => { EncryptionAlgorithmRegistry.RegisterCustom("x",null!); }); }
    [Fact] public void REG06_InvalidCustomNamesAreRejected() { Assert.Throws<ArgumentException>(() => { CompressionAlgorithmRegistry.RegisterCustom("",()=>new CustomCompression()); }); Assert.Throws<ArgumentException>(() => { ChecksumAlgorithmRegistry.RegisterCustom(" ",()=>new TestChecksum("x")); }); Assert.Throws<ArgumentException>(() => { EncryptionAlgorithmRegistry.RegisterCustom("",()=>new CustomEncryption()); }); Assert.Throws<ArgumentException>(() => { CompressionAlgorithmRegistry.RegisterCustom(" ",()=>new CustomCompression()); }); Assert.Throws<ArgumentException>(() => { EncryptionAlgorithmRegistry.RegisterCustom(" ",()=>new CustomEncryption()); }); }
    [Fact] public void REG07_SecondCustomRegistrationReplacesFirst()
    {
        const string n="qa-replace"; CompressionAlgorithmRegistry.RegisterCustom(n,()=>new MarkerCompression()); CompressionAlgorithmRegistry.RegisterCustom(n,()=>new CustomCompression()); Assert.IsType<CustomCompression>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.Custom,n));
    }
    [Fact] public void REG08_ConcurrentCustomRegistryAccessIsSafe()
    {
        Parallel.For(0, 100, (int i) =>{var n=$"qa-concurrent-{i}";CompressionAlgorithmRegistry.RegisterCustom(n,()=>new CustomCompression());Assert.IsType<CustomCompression>(CompressionAlgorithmRegistry.Resolve(CompressionAlgorithm.Custom,n));});
    }
}

internal sealed class MarkerCompression : ICompressionAlgorithm
{
    public CompressionAlgorithm Kind=>CompressionAlgorithm.None; public string? CustomName=>null; public int GetMaxCompressedLength(int n)=>n; public int Compress(ReadOnlySpan<byte>s,Span<byte>d){s.CopyTo(d);return s.Length;} public int Decompress(ReadOnlySpan<byte>s,Span<byte>d){s.CopyTo(d);return s.Length;}
}
internal sealed class CustomCompression : ICompressionAlgorithm
{
    public CompressionAlgorithm Kind=>CompressionAlgorithm.Custom; public string? CustomName=>"qa-compression"; public int GetMaxCompressedLength(int n)=>n; public int Compress(ReadOnlySpan<byte>s,Span<byte>d){s.CopyTo(d);return s.Length;} public int Decompress(ReadOnlySpan<byte>s,Span<byte>d){s.CopyTo(d);return s.Length;}
}
internal sealed class CustomEncryption : IEncryptionAlgorithm
{
    public EncryptionAlgorithm Kind=>EncryptionAlgorithm.Custom; public string? CustomName=>"qa-encryption"; public int GetMaxCiphertextLength(int n)=>n; public int Encrypt(ReadOnlySpan<byte>s,ReadOnlySpan<byte>k,Span<byte>d){s.CopyTo(d);return s.Length;} public int Decrypt(ReadOnlySpan<byte>s,ReadOnlySpan<byte>k,Span<byte>d){s.CopyTo(d);return s.Length;}
}
