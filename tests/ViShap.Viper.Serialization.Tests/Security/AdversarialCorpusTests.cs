namespace ViShap.Viper.Serialization.Tests.Security;

public sealed class AdversarialCorpusTests
{
    private static BinarySerializerOptions V0 => BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().WithLimits(TestHelpers.TightOptions().Limits).Build();

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(int.MaxValue)] [InlineData(int.MinValue)]
    public void FuzzedArrayCountsAreBounded(int count)
    {
        var bytes=WriteV0RootCount(count, includeIntElements:true); var ex=Record.Exception(()=>new BinarySerializer(V0).Deserialize<int[]>(bytes));
        if(count>=0&&count<=3) Assert.Null(ex); else Assert.IsType<BinaryFormatException>(ex);
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(int.MaxValue)]
    public void FuzzedListCountsAreBounded(int count)
    {
        var bytes=WriteV0RootCount(count, includeIntElements:true); var ex=Record.Exception(()=>new BinarySerializer(V0).Deserialize<List<int>>(bytes));
        if(count>=0&&count<=3) Assert.Null(ex); else Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact] public void CumulativeTotalElementsRejectsNestedAmplification()
    {
        var source=new List<int[]>{new[]{1,2,3},new[]{4,5,6}}; var bytes=new BinarySerializer(BinarySerializerOptions.Configure().WithLimits(new DeserializationLimits{MaxDepth=8,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=2,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=5,MaxMessageBytes=1024}).Build()).Serialize(source);
        Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(TestHelpers.TightOptions()).Deserialize<List<int[]>>(bytes));
    }

    [Fact] public void DictionaryCountAboveLimitIsRejectedBeforeReadingEntries() { var b=WriteV0RootCount(3); Assert.IsType<BinaryFormatException>(Record.Exception(()=>new BinarySerializer(V0).Deserialize<Dictionary<int,int>>(b))); }

    [Fact] public void MultiDimensionalProductAboveLimitIsRejectedBeforeAllocation()
    {
        using var ms=new MemoryStream(); using var w=new BinaryWriter(ms); w.Write(true); w.Write(2); w.Write(2); w.Write(3); w.Flush(); Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(V0).Deserialize<int[,]>(ms.ToArray()));
    }

    [Fact] public void NegativeMultiDimensionalDimensionIsRejected() { using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(true);w.Write(2);w.Write(-1);w.Write(1);w.Flush();Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(V0).Deserialize<int[,]>(ms.ToArray())); }

    [Fact] public void BitCountAboveBlobBudgetIsRejected() { using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(true);w.Write(65);w.Flush();Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(V0).Deserialize<BitArray>(ms.ToArray())); }

    [Fact] public void StringAboveLimitIsRejectedBeforePayloadRead()
    {
        using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(true);w.Write7BitEncodedInt(9);w.Write(new byte[9]);w.Flush();Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(V0).Deserialize<string>(ms.ToArray()));
    }

    [Fact] public void StringMalformedVarintIsBounded()
    {
        using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(true);w.Write(new byte[]{0x80,0x80,0x80,0x80,0x80,0x00});w.Flush();var ex=Record.Exception(()=>new BinarySerializer(V0).Deserialize<string>(ms.ToArray()));Assert.NotNull(ex);Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact] public void BlobLimitIsEnforcedByBigIntegerFormatter()
    {
        var options=TestHelpers.TightOptions(); using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(9);w.Write(new byte[9]);w.Flush();var ex=Record.Exception(()=>new BinarySerializer(options).Deserialize<BigInteger>(ms.ToArray()));Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact] public void TruncationSweepHasNoProcessFatalOutcome()
    {
        var bytes=new BinarySerializer().Serialize(new Person{Name="truncate",Age=42,Address=new Address{City="c",Street="s"}});
        for(int i=1;i<bytes.Length;i++) { var ex=Record.Exception(()=>new BinarySerializer().Deserialize<Person>(bytes[..i])); Assert.NotNull(ex); Assert.True(ex is BinarySerializerException or EndOfStreamException or IOException or ArgumentException, $"{i}: {ex.GetType()}"); }
    }

    [Fact] public void SingleByteMutationOfChecksummedPayloadFailsIntegrity()
    {
        var options=BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build(); var bytes=new BinarySerializer(options).Serialize(new Person{Name="payload"});
        for(int i=29;i<bytes.Length;i++) { var mutated=bytes.ToArray();mutated[i]^=1; Assert.IsType<BinaryIntegrityException>(Record.Exception(()=>new BinarySerializer(options).Deserialize<Person>(mutated))); }
    }

    private static byte[] WriteV0RootCount(int count, bool includeIntElements=false) { using var ms=new MemoryStream();using var w=new BinaryWriter(ms);w.Write(true);w.Write(count);if(includeIntElements&&count>=0&&count<=3)for(var i=0;i<count;i++)w.Write(i);w.Flush();return ms.ToArray(); }
}
