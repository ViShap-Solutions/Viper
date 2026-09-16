using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Exceptions;

public sealed class ExceptionTaxonomyTests
{
    [Fact] public void BinaryFormatException_MalformedHeader() { var b=new BinarySerializer().Serialize(1); b[0]^=1; Assert.IsType<BinaryFormatException>(Record.Exception(()=>new BinarySerializer().Deserialize<int>(b))); }
    [Fact] public void BinaryFormatNotSupported_UnknownAlgorithm() { var b=new BinarySerializer().Serialize(1); b[8]=250; Assert.IsType<BinaryFormatNotSupportedException>(Record.Exception(()=>new BinarySerializer().Deserialize<int>(b))); }
    [Fact] public void BinaryIntegrity_ChecksumMismatch() { var options=BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build(); var b=new BinarySerializer(options).Serialize(1); b[^1]^=1; Assert.IsType<BinaryIntegrityException>(Record.Exception(()=>new BinarySerializer(options).Deserialize<int>(b))); }
    [Fact] public void BinaryIntegrity_KeyFailure() { var k=new byte[32]; var w=new BinarySerializer(BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),k).Build()); var b=w.Serialize(1); Assert.IsType<BinaryIntegrityException>(Record.Exception(()=>new BinarySerializer(BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),Enumerable.Repeat((byte)1,32).ToArray()).Build()).Deserialize<int>(b))); }
    [Fact] public void BinaryType_InvalidContract() => Assert.Throws<BinaryTypeException>(() => { TypeAccessorCache.GetOrBuild(typeof(InvalidContractFixture)); });
    [Fact] public void BinaryType_Delegate() => Assert.Throws<BinaryTypeException>(() => { new BinarySerializer().Serialize<Action>(()=>{}); });
    [Fact] public void ArgumentNull_PublicArguments() { Assert.Throws<ArgumentNullException>(() => { new BinarySerializer().Deserialize<int>((byte[])null!); }); Assert.Throws<ArgumentNullException>(() => { BinaryFormatInspector.Peek(null!); }); }
    [Fact] public void ArgumentException_InvalidKey() => Assert.Throws<ArgumentException>(() => { new Aes256Gcm().Encrypt(Array.Empty<byte>(),new byte[1],new byte[64]); });
    [Fact] public void BclStreamException_NonReadable() { using var s=new NonReadable(); Assert.ThrowsAny<NotSupportedException>(()=>new BinarySerializer().Deserialize<int>(s)); }
    private sealed class NonReadable : Stream { public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>0;public override long Position{get=>0;set=>throw new NotSupportedException();}public override void Flush(){}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin so)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException(); }
}
