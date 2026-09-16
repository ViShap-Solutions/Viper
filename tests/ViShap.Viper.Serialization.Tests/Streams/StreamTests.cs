using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Streams;

public sealed class StreamBehaviorTests
{
    [Fact] public void STR01_MemoryStreamRoundTrip()
    {
        var serializer=new BinarySerializer(); using var s=new MemoryStream(); serializer.Serialize(s,new Person{Name="p"}); s.Position=0; var actual=serializer.Deserialize<Person>(s); Assert.Equal("p",actual!.Name);
    }
    [Fact] public void STR02_CallerOwnedStreamRemainsOpen()
    {
        using var s=new MemoryStream(); new BinarySerializer().Serialize(s,1); Assert.True(s.CanRead&&s.CanWrite&&s.CanSeek); s.Position=0; Assert.Equal(1,new BinarySerializer().Deserialize<int>(s)); Assert.True(s.CanRead);
    }
    [Fact] public void STR03_PositionAdvancesByConsumedPayload()
    {
        var bytes=new BinarySerializer().Serialize(1); using var s=new MemoryStream(bytes); new BinarySerializer().Deserialize<int>(s); Assert.Equal(bytes.Length,s.Position);
    }
    [Fact] public void STR04_NonSeekableStreamRequiresNotSupported()
    {
        var bytes=new BinarySerializer().Serialize(1); using var s=new NonSeekableReadStream(bytes); Assert.Throws<NotSupportedException>(()=>new BinarySerializer().Deserialize<int>(s));
    }
    [Fact] public void STR05_PartialReadsShouldEventuallyProduceCompleteMessage()
    {
        var bytes=new BinarySerializer().Serialize(new Person{Name="partial",Age=4}); using var s=new PartialReadStream(bytes,2); var actual=new BinarySerializer().Deserialize<Person>(s); Assert.Equal("partial",actual!.Name);
    }
    [Fact] public void STR06_PrematureEofIsFormatError()
    {
        var bytes=new BinarySerializer().Serialize(1); using var s=new MemoryStream(bytes[..^1]); Assert.Throws<BinaryFormatException>(()=>new BinarySerializer().Deserialize<int>(s));
    }
    [Fact] public void STR07_ReadOnlyStreamSupportsDeserialization()
    {
        var bytes=new BinarySerializer().Serialize(123); using var s=new MemoryStream(bytes,writable:false); Assert.Equal(123,new BinarySerializer().Deserialize<int>(s));
    }
    [Fact] public void STR08_NonReadableStreamFailsWithBclCapabilityException()
    {
        using var s=new NonReadableStream(); Assert.ThrowsAny<NotSupportedException>(()=>new BinarySerializer().Deserialize<int>(s));
    }
}

internal sealed class PartialReadStream(byte[] data,int maxChunk) : MemoryStream(data)
{
    public override int Read(byte[] buffer,int offset,int count) => base.Read(buffer,offset,Math.Min(count,maxChunk));
    public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length,maxChunk)]);
}

internal sealed class NonSeekableReadStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek=>false;
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override long Position {get=>base.Position;set=>throw new NotSupportedException();}
}

internal sealed class NonReadableStream : Stream
{
    public override bool CanRead=>false; public override bool CanSeek=>false; public override bool CanWrite=>false; public override long Length=>0; public override long Position {get=>0;set=>throw new NotSupportedException();}
    public override void Flush(){} public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException(); public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException(); public override void SetLength(long value)=>throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
