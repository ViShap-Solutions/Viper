namespace ViShap.Viper.Serialization.Tests.Format;

public sealed class HeaderSecurityTests
{
    private static readonly BinarySerializer Serializer = new();
    private static byte[] Valid() => Serializer.Serialize(123);

    [Fact] public void HDR01_MagicMismatchIsBinaryFormatException() { var b=Valid(); b[0]^=0xFF; Assert.Throws<BinaryFormatException>(()=>Serializer.Deserialize<int>(b)); }
    [Fact] public void HDR02_UnsupportedVersionFollowsRoutingContract() { var b=Valid(); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4),99); Assert.Throws<BinaryFormatNotSupportedException>(()=>Serializer.Deserialize<int>(b)); }
    [Fact]
    public void HDR03_FixedHeaderTruncationAlwaysThrowsBinaryFormatException()
    {
        var bytes = Valid();
        
        for (var length = 1; length < 29; length++)
        {
            var truncated = bytes[..length];

            var exception = Record.Exception(
                () => Serializer.Deserialize<int>(truncated));

            Assert.NotNull(exception);
            Assert.IsType<BinaryFormatException>(exception);
        }
    }
    [Fact] public void HDR05_InvalidAlgorithmEnumsAreRejected()
    {
        foreach(var offset in new[]{8,10,12}) { var b=Valid(); b[offset]=254; Assert.Throws<BinaryFormatNotSupportedException>(()=>Serializer.Deserialize<int>(b)); }
    }
    [Fact] public void HDR06_NoCompressionRequiresEqualLengths()
    {
        var b=Valid(); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(20))+1); Assert.Throws<BinaryFormatException>(()=>Serializer.Deserialize<int>(b));
    }
    [Fact] public void HDR07_NoEncryptionRequiresEqualLengths()
    {
        var b=Valid(); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(24))+1); Assert.Throws<BinaryFormatException>(()=>Serializer.Deserialize<int>(b));
    }
    [Fact] public void HDR08_ChecksumLengthTruncationIsFormatError()
    {
        var b=Valid(); b[28]=4; Assert.Throws<BinaryFormatException>(()=>Serializer.Deserialize<int>(b));
    }
    [Fact] public void HDR09_PayloadTruncationIsFormatError()
    {
        var b=Valid(); Assert.Throws<BinaryFormatException>(()=>Serializer.Deserialize<int>(b[..^1]));
    }
    [Fact] public void HDR10_TrailingBytesAreCurrentlyAcceptedAndStable()
    {
        var b=Valid().Concat(new byte[]{0xDE,0xAD}).ToArray(); Assert.Equal(123,Serializer.Deserialize<int>(b));
    }
    [Fact] public void HDR11_PeekPreservesPosition()
    {
        var b = Valid();
        using var s = new MemoryStream(b);
        s.Position = 0;
        var before = s.Position;
        var info = BinaryFormatInspector.Peek(s);
        Assert.Equal(before, s.Position);
        Assert.NotNull(info);
        Assert.Equal(1, info.Value.FormatVersion);
    }
    [Fact] public void HDR12_UnknownStreamDoesNotPretendToBeHeader() { using var s=new MemoryStream(Enumerable.Repeat((byte)0xAA,64).ToArray()); Assert.Null(BinaryFormatInspector.Peek(s)); }
}

public sealed class InspectorTests
{
    [Fact] public void INS01_ValidV1HeaderExposesMetadata()
    {
        var key=new byte[32]; var options=BinarySerializerOptions.Configure().WithCompression(new Deflate()).WithChecksum(new Crc32()).WithEncryption(new Aes256Gcm(),key,"key-A").Build(); var bytes=new BinarySerializer(options).Serialize(1); using var s=new MemoryStream(bytes); var info=BinaryFormatInspector.Peek(s); Assert.Equal(1,info!.Value.FormatVersion); Assert.Equal(CompressionAlgorithm.Deflate,info.Value.Compression); Assert.Equal(ChecksumAlgorithm.Crc32,info.Value.ChecksumAlgorithm); Assert.Equal(EncryptionAlgorithm.Aes256Gcm,info.Value.Encryption); Assert.Equal("key-A",info.Value.KeyId);
    }
    [Fact] public void INS02_PositionIsPreserved() { var bytes=new BinarySerializer().Serialize(1); using var s=new MemoryStream(bytes); s.Position=5; BinaryFormatInspector.Peek(s); Assert.Equal(5,s.Position); }
    [Fact] public void INS03_NonSeekableStreamThrowsNotSupported() { using var s=new NonSeekableStream(new byte[16]); Assert.Throws<NotSupportedException>(()=>BinaryFormatInspector.Peek(s)); }
    [Fact] public void INS04_UnrecognizedDataReturnsNull() { using var s=new MemoryStream(new byte[]{1,2,3}); Assert.Null(BinaryFormatInspector.Peek(s)); }
    [Fact] public void INS05_FromHeaderResolvesConfiguredAlgorithms()
    {
        var info=new BinaryHeaderInfo(1,CompressionAlgorithm.None,null,ChecksumAlgorithm.None,null,EncryptionAlgorithm.None,null,null); var options=BinarySerializerOptions.FromHeader(info); Assert.Equal(CompressionAlgorithm.None,options.Compressor.DefaultKind); Assert.Equal(ChecksumAlgorithm.None,options.Checksum.DefaultKind); Assert.Equal(EncryptionAlgorithm.None,options.Encryptor.DefaultKind);
    }
    [Fact] public void INS06_FromStreamUnknownInputIsFormatError() { using var s=new MemoryStream(new byte[]{1,2,3}); Assert.Throws<BinaryFormatException>(()=>BinarySerializerOptions.FromStream(s)); }
    [Fact] public void INS07_FromStreamPreservesPosition()
    {
        var bytes=new BinarySerializer().Serialize(5); using var s=new MemoryStream(bytes); var p=s.Position; var options=BinarySerializerOptions.FromStream(s); Assert.Equal(p,s.Position); Assert.Equal(CompressionAlgorithm.None,options.Compressor.DefaultKind);
    }
}

internal sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override long Position { get => base.Position; set => throw new NotSupportedException(); }
}
