namespace ViShap.Viper.Serialization.Tests.Algorithms;

public sealed class CompressionTests
{
    [Fact] public void CMP01_NoCompressionRoundTripAndLengths()
    {
        var a = new NoCompression();
        var data = Encoding.UTF8.GetBytes("abc");
        var compressed = new byte[data.Length];

        var compressedLength = a.Compress(data, compressed);
        Assert.Equal(data.Length, compressedLength);
        Assert.Equal(data, compressed);

        var decompressed = new byte[data.Length];
        var decompressedLength = a.Decompress(compressed, decompressed);
        Assert.Equal(data.Length, decompressedLength);
        Assert.Equal(data, decompressed);
    }
    [Fact] public void CMP02_DeflateRoundTripMatrix() => AssertCompression(new Deflate());
    [Fact] public void CMP03_BrotliRoundTripMatrix() => AssertCompression(new Brotli());
    private static void AssertCompression(ICompressionAlgorithm algorithm)
    {
        foreach(var data in new[]{Array.Empty<byte>(),new byte[]{1,2,3},Enumerable.Repeat((byte)65,1024).ToArray(),Enumerable.Range(0,1024).Select(i=>(byte)i).ToArray()})
        {
            var compressed=new byte[algorithm.GetMaxCompressedLength(data.Length)]; var written=algorithm.Compress(data,compressed); var output=new byte[data.Length]; var read=algorithm.Decompress(compressed.AsSpan(0,written),output); Assert.Equal(data.Length,read); Assert.Equal(data,output);
        }
    }
    [Fact] public void CMP04_IncorrectExpectedDecompressedLengthIsRejected()
    {
        var c=new Compressor(new Deflate()); var data=Encoding.UTF8.GetBytes("hello world"); var compressed=c.Compress(data); Assert.Throws<BinaryFormatException>(()=>c.Decompress(CompressionAlgorithm.Deflate,null,compressed,data.Length+1));
    }
    [Fact] public void CMP05_DeclaredExpansionAboveMessageLimitIsRejectedBeforeAllocation()
    {
        var limits=TestHelpers.TightOptions().Limits; var c=new Compressor(new Deflate(),limits); Assert.Throws<BinaryFormatException>(()=>c.Decompress(CompressionAlgorithm.Deflate,null,Array.Empty<byte>(),limits.MaxMessageBytes+1>int.MaxValue?int.MaxValue:(int)limits.MaxMessageBytes+1));
    }
}

public sealed class ChecksumTests
{
    [Fact] public void CHK01_NoChecksumProducesZeroLength() { var c=ChecksumCalculator.None; Assert.Empty(c.Compute(new byte[]{1,2,3})); }
    [Fact] public void CHK02_Crc32IsFourBytesAndRoundTrips() { var c=new ChecksumCalculator(new Crc32()); var p=Encoding.UTF8.GetBytes("payload"); var h=c.Compute(p); Assert.Equal(4,h.Length); c.Verify(ChecksumAlgorithm.Crc32,null,p,h); }
    [Fact] public void CHK03_MismatchIsIntegrityFailure() { var c=new ChecksumCalculator(new Crc32()); var p=Encoding.UTF8.GetBytes("payload"); var h=c.Compute(p); h[0]^=1; Assert.Throws<BinaryIntegrityException>(()=>c.Verify(ChecksumAlgorithm.Crc32,null,p,h)); }
    [Fact] public void CHK04_WrongLengthIsIntegrityFailure() { var c=new ChecksumCalculator(new Crc32()); Assert.Throws<BinaryIntegrityException>(()=>c.Verify(ChecksumAlgorithm.Crc32,null,new byte[]{1},new byte[3])); }
    [Fact] public void CHK05_CustomChecksumRegistrationAndPipeline()
    {
        const string name="qa-checksum"; ChecksumAlgorithmRegistry.RegisterCustom(name,()=>new TestChecksum(name)); var options=BinarySerializerOptions.Configure().WithChecksum(new TestChecksum(name)).Build(); var s=new BinarySerializer(options); var bytes=s.Serialize(123); using var ms=new MemoryStream(bytes); var info=BinaryFormatInspector.Peek(ms); Assert.Equal(ChecksumAlgorithm.Custom,info!.Value.ChecksumAlgorithm); Assert.Equal(name,info.Value.CustomChecksumName); Assert.Equal(123,s.Deserialize<int>(bytes));
    }
    [Fact] public void CHK06_MissingCustomChecksumFails() => Assert.Throws<BinaryFormatNotSupportedException>(()=>new ChecksumCalculator(new Crc32()).Verify(ChecksumAlgorithm.Custom,"definitely-missing",new byte[]{1},new byte[4]));
}

public sealed class EncryptionTests
{
    [Fact] public void ENC01_Valid32ByteKeyRoundTrip()
    {
        var key=new byte[32]; for(var i=0;i<key.Length;i++) key[i]=(byte)i; var e=new Aes256Gcm(); var src=Encoding.UTF8.GetBytes("secret"); var buf=new byte[e.GetMaxCiphertextLength(src.Length)]; var n=e.Encrypt(src,key,buf); var outBuf=new byte[src.Length]; Assert.Equal(src.Length,e.Decrypt(buf.AsSpan(0,n),key,outBuf)); Assert.Equal(src,outBuf);
    }
    [Fact] public void ENC02_WrongKeyIsIntegrityFailure()
    {
        var key=new byte[32]; var e=new Aes256Gcm(); var src=new byte[]{1,2,3}; var c=new byte[e.GetMaxCiphertextLength(src.Length)]; var n=e.Encrypt(src,key,c); Assert.Throws<BinaryIntegrityException>(()=>e.Decrypt(c.AsSpan(0,n),Enumerable.Repeat((byte)1,32).ToArray(),new byte[src.Length]));
    }
    [Fact] public void ENC03_CiphertextTamperingIsIntegrityFailure()
    {
        var key=new byte[32]; var e=new Aes256Gcm(); var c=new byte[e.GetMaxCiphertextLength(3)]; var n=e.Encrypt(new byte[]{1,2,3},key,c); c[n-1]^=1; Assert.Throws<BinaryIntegrityException>(()=>e.Decrypt(c.AsSpan(0,n),key,new byte[3]));
    }
    [Fact] public void ENC04_KeyIdMismatchFailsBeforeDecrypt()
    {
        var key=new byte[32]; var write=BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),key,"key-A").Build(); var read=BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),key,"key-B").Build(); var bytes=new BinarySerializer(write).Serialize(7); Assert.Throws<BinaryIntegrityException>(()=>new BinarySerializer(read).Deserialize<int>(bytes));
    }
    [Fact] public void ENC05_ExpectedPlaintextAboveLimitIsRejectedBeforeAllocation() { var e=new Encryptor(new Aes256Gcm(),new byte[32],limits:TestHelpers.TightOptions().Limits); Assert.Throws<BinaryFormatException>(()=>e.Decrypt(EncryptionAlgorithm.Aes256Gcm,null,Array.Empty<byte>(),33)); }
    [Fact] public void ENC06_InvalidKeyLengthIsArgumentError() { var e=new Aes256Gcm(); Assert.Throws<ArgumentException>(()=>e.Encrypt(Array.Empty<byte>(),new byte[31],new byte[64])); }
    [Fact] public void ENC07_FixedKeyIsClearedOnDispose() { var key=Enumerable.Repeat((byte)0xA5,32).ToArray(); var e=new Encryptor(new Aes256Gcm(),key); e.Dispose(); Assert.All(key,b=>Assert.Equal((byte)0,b)); }
    [Fact] public void ENC08_ResolverKeyIsClearedAfterOperation()
    {
        var key=Enumerable.Repeat((byte)0xA5,32).ToArray(); var captured=key; var e=new Encryptor(new Aes256Gcm(),_=>captured); e.Encrypt(new byte[]{1,2,3}); Assert.All(captured,b=>Assert.Equal((byte)0,b));
    }
    [Fact] public void ENC09_FixedKeyRemainsUsableUntilDispose() { var key=Enumerable.Repeat((byte)7,32).ToArray(); var e=new Encryptor(new Aes256Gcm(),key); Assert.NotEmpty(e.Encrypt(new byte[]{1})); Assert.NotEmpty(e.Encrypt(new byte[]{2})); e.Dispose(); }
}

internal sealed class TestChecksum(string name) : IChecksumAlgorithm
{
    public ChecksumAlgorithm Kind=>ChecksumAlgorithm.Custom; public string? CustomName=>name; public int HashSizeInBytes=>1; public void Compute(ReadOnlySpan<byte> source,Span<byte> destination){destination[0]=0;foreach(var b in source)destination[0]^=b;}
}
