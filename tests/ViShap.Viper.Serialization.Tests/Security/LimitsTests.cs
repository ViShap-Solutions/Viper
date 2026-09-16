namespace ViShap.Viper.Serialization.Tests.Security;

public sealed class LimitsValidationTests
{
    public static IEnumerable<object[]> LimitProperties()
    {
        yield return new object[]{ nameof(DeserializationLimits.MaxDepth), new Func<DeserializationLimits,int>(x=>x.MaxDepth), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxDepth=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxArrayLength), new Func<DeserializationLimits,int>(x=>x.MaxArrayLength), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxArrayLength=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxCollectionLength), new Func<DeserializationLimits,int>(x=>x.MaxCollectionLength), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxCollectionLength=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxDictionaryEntries), new Func<DeserializationLimits,int>(x=>x.MaxDictionaryEntries), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxDictionaryEntries=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxStringLength), new Func<DeserializationLimits,int>(x=>x.MaxStringLength), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxStringLength=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxByteBlobLength), new Func<DeserializationLimits,int>(x=>x.MaxByteBlobLength), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxByteBlobLength=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxTotalElements), new Func<DeserializationLimits,int>(_=>1), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxTotalElements=0}) };
        yield return new object[]{ nameof(DeserializationLimits.MaxMessageBytes), new Func<DeserializationLimits,int>(_=>1), new Func<DeserializationLimits,DeserializationLimits>(x=>x with {MaxMessageBytes=0}) };
    }

    [Theory]
    [MemberData(nameof(LimitProperties))]
    public void ZeroValuesAreRejected(string _, Func<DeserializationLimits,int> __, Func<DeserializationLimits,DeserializationLimits> mutate) => Assert.Throws<BinaryTypeException>(()=>mutate(DeserializationLimits.Default).Validate());

    [Fact] public void PositiveLimitsValidate() => DeserializationLimits.Default.Validate();

    [Fact] public void NegativeLimitsAreRejected()
    {
        Assert.Throws<BinaryTypeException>(()=>(DeserializationLimits.Default with {MaxDepth=-1}).Validate());
        Assert.Throws<BinaryTypeException>(()=>(DeserializationLimits.Default with {MaxTotalElements=-1}).Validate());
    }
}

public sealed class BudgetInvariantTests
{
    private static DeserializationBudget Budget(long total=5,int depth=4) => new(new DeserializationLimits{MaxDepth=depth,MaxTotalElements=total,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=2,MaxStringLength=8,MaxByteBlobLength=8,MaxMessageBytes=32});

    [Fact] public void TotalElementsNeverDecreases() { var b=Budget(); b.ConsumeElements(3); Assert.Equal(0,b.Depth); Assert.Throws<BinaryFormatException>(()=>b.ConsumeElements(3)); }
    [Fact] public void ExactTotalBoundarySucceeds() { var b=Budget(); b.ConsumeElements(5); }
    [Fact] public void AboveTotalBoundaryFails() { var b=Budget(); b.ConsumeElements(5); Assert.Throws<BinaryFormatException>(()=>b.ConsumeElements(1)); }
    [Fact] public void NegativeElementCountFails() => Assert.Throws<BinaryFormatException>(()=>Budget().ConsumeElements(-1));
    [Fact] public void DepthUnwindsOnDisposeAndDoubleDisposeIsSafe() { var b=Budget(); using(var scope=b.EnterDepth()) Assert.Equal(1,b.Depth); Assert.Equal(0,b.Depth); using(var scope=b.EnterDepth()){scope.Dispose();scope.Dispose();} Assert.Equal(0,b.Depth); }
    [Fact] public void FailedEnterDepthDoesNotLeakDepth() { var b=Budget(depth:1); using var s=b.EnterDepth(); Assert.Throws<BinaryFormatException>(()=>b.EnterDepth()); Assert.Equal(1,b.Depth); s.Dispose(); Assert.Equal(0,b.Depth); }
    [Fact] public void ByteBudgetRejectsNegativeAndOverMaximum() { var b=Budget(); Assert.Throws<BinaryFormatException>(()=>b.ConsumeBytes(-1)); Assert.Throws<BinaryFormatException>(()=>b.ConsumeBytes(33)); b.ConsumeBytes(32); }
}

public sealed class GuardTests
{
    private static BinaryPayloadReader Reader(long total=5)
    {
        var limits=new DeserializationLimits{MaxDepth=4,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=2,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=total,MaxMessageBytes=32};
        return new BinaryPayloadReader(new BinaryReader(new MemoryStream()),false,limits);
    }
    [Fact] public void ValidateCountBoundaryAndOverflow() { var r=Reader(); Assert.Equal(3,DeserializationGuard.ValidateCount(r,3,3,"array")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateCount(r,4,3,"array")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateCount(r,-1,3,"array")); }
    [Fact] public void ValidateLengthBoundaryAndOverflow() { DeserializationGuard.ValidateLength(8,8,"blob"); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateLength(9,8,"blob")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateLength(-1,8,"blob")); }
    [Fact] public void ValidateTotalElementsHandlesZeroAndOverflow() { var r=Reader(); Assert.Equal(0,DeserializationGuard.ValidateTotalElements(r,new[]{0,int.MaxValue},5,"array")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateTotalElements(Reader(),new[]{2,3},5,"array")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateTotalElements(Reader(),new[]{-1},5,"array")); }
    [Fact] public void ReadValidatedBytesRejectsLengthBeforeRead() { using var ms=new MemoryStream(new byte[]{1,2}); var r=new BinaryPayloadReader(new BinaryReader(ms),false,new DeserializationLimits{MaxDepth=4,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=2,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=5,MaxMessageBytes=32}); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ReadValidatedBytes(r,9,8,"blob")); }
    [Fact] public void ReadValidatedBytesRejectsPrematureEof() { using var ms=new MemoryStream(new byte[]{1,2}); var r=new BinaryPayloadReader(new BinaryReader(ms),false,new DeserializationLimits{MaxDepth=4,MaxArrayLength=3,MaxCollectionLength=3,MaxDictionaryEntries=2,MaxStringLength=8,MaxByteBlobLength=8,MaxTotalElements=5,MaxMessageBytes=32}); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ReadValidatedBytes(r,3,8,"blob")); }

    [Fact] public void BitCountBoundaries() { Assert.Equal(64,DeserializationGuard.ValidateBitCount(64,8,"bits")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateBitCount(65,8,"bits")); Assert.Throws<BinaryFormatException>(()=>DeserializationGuard.ValidateBitCount(-1,8,"bits")); }
}
