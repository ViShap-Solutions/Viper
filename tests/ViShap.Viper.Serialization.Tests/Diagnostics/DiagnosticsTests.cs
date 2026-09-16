namespace ViShap.Viper.Serialization.Tests.Diagnostics;

public sealed class DiagnosticsTests
{
    [Fact] public void DMP01_ValidHeaderDump()
    {
        var bytes=new BinarySerializer().Serialize(42); var text=BinaryFormatDumper.DumpHeader(new MemoryStream(bytes)); Assert.Contains("FormatVersion    : 1",text,StringComparison.Ordinal);
    }
    [Fact] public void DMP02_UnknownInput()
    {
        var text=BinaryFormatDumper.DumpHeader(new MemoryStream(new byte[]{1,2,3})); Assert.NotNull(text);
    }
    [Fact] public void DMP03_SuccessfulObjectDump()
    {
        var bytes=new BinarySerializer().Serialize(new ViShap.Viper.Serialization.Tests.Fixtures.Person{Name="dump",Age=1,Address=new ViShap.Viper.Serialization.Tests.Fixtures.Address{City="c",Street="s"}}); var text=BinaryFormatDumper.Dump<object>(bytes); Assert.NotNull(text);
    }
    [Fact] public void DMP04_FailedParseDumpIsDiagnostic()
    {
        var bytes=new BinarySerializer().Serialize(42); Array.Resize(ref bytes,bytes.Length-1); var text=BinaryFormatDumper.Dump<int>(bytes); Assert.NotNull(text);
    }
    [Fact] public void DMP05_TraceFieldsAreStructured()
    {
        using var ms=new MemoryStream(); using var bw=new BinaryWriter(ms,Encoding.UTF8,true); var writer=new BinaryPayloadWriter(bw); writer.Serialize(42); bw.Flush(); ms.Position=0; using var br=new BinaryReader(ms,Encoding.UTF8,true); var reader=new BinaryPayloadReader(br,enableTrace:true); Assert.Equal(42,reader.Deserialize<int>()); Assert.All(reader.Trace,e=>{Assert.True(e.Offset>=0);Assert.True(e.Depth>=0);Assert.False(string.IsNullOrWhiteSpace(e.TypeName));});
    }
    [Fact] public void DMP06_TraceRingBufferIsBoundedAt500()
    {
        using var ms=new MemoryStream(); using var bw=new BinaryWriter(ms,Encoding.UTF8,true); var writer=new BinaryPayloadWriter(bw); writer.Serialize(Enumerable.Range(0,600).ToList()); bw.Flush(); ms.Position=0; using var br=new BinaryReader(ms,Encoding.UTF8,true); var reader=new BinaryPayloadReader(br,enableTrace:true); reader.Deserialize<List<int>>(); Assert.Equal(500,reader.Trace.Count); Assert.Equal("List`1",reader.Trace[^1].TypeName);
    }
    [Fact] public void DMP07_SharedReferenceMarkerTerminatesGraphDump()
    {
        var p=new Person{Name="p"}; var g=new SharedReferenceGraph{Home=p,Work=p}; var text=ObjectGraphDumper.Dump(g); Assert.Contains("shared",text,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void DMP08_CycleGraphTerminates()
    {
        var n=new CyclicNode{Name="cycle"}; n.Next=n; var text=ObjectGraphDumper.Dump(n); Assert.NotNull(text); Assert.True(text.Length<10000);
    }
}
