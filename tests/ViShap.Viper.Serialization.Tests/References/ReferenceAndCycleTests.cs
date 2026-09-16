namespace ViShap.Viper.Serialization.Tests.References;

public sealed class ReferencePreservationTests
{
    private static BinarySerializerOptions Preserve => BinarySerializerOptions.Configure().PreserveReferences().Build();

    [Fact] public void REF01_SharedDagPreservesIdentity()
    {
        var shared=new Person{Name="shared"}; var value=new SharedReferenceGraph{Home=shared,Work=shared}; var actual=TestHelpers.RoundTrip(value,Preserve); Assert.Same(actual.Home,actual.Work);
    }

    [Fact] public void REF02_DefaultModeDoesNotPreserveIdentity()
    {
        var shared=new Person{Name="shared"}; var actual=TestHelpers.RoundTrip(new SharedReferenceGraph{Home=shared,Work=shared}); Assert.NotSame(actual.Home,actual.Work); Assert.Equal(actual.Home!.Name,actual.Work!.Name);
    }

    [Fact] public void REF03_RepeatedCollectionReferencePreservesAllThree()
    {
        var shared=new Person{Name="shared"}; var value=new List<Person>{shared,shared,shared}; var actual=TestHelpers.RoundTrip(value,Preserve); Assert.Same(actual[0],actual[1]); Assert.Same(actual[1],actual[2]);
    }

    [Fact] public void REF04_HeaderPreserveFlagControlsInterpretation()
    {
        var shared=new Person{Name="x"}; var write=new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences(false).Build()); var bytes=write.Serialize(new SharedReferenceGraph{Home=shared,Work=shared});
        var read=new BinarySerializer(Preserve).Deserialize<SharedReferenceGraph>(bytes); Assert.NotSame(read!.Home,read.Work);
    }

    [Fact] public void REF05_InvalidReferenceMarkerIsRejected()
    {
        var options=Preserve; var bytes=new BinarySerializer(options).Serialize(new SharedReferenceGraph{Home=new Person{Name="a"},Work=new Person{Name="b"}});
        var idx=FindFirstObjectMarker(bytes); bytes[idx]=2;
        Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(options).Deserialize<SharedReferenceGraph>(bytes));
    }

    [Fact] public void REF06_InvalidReferenceIdIsRejected()
    {
        var options=Preserve; var bytes=new BinarySerializer(options).Serialize(new SharedReferenceGraph{Home=new Person{Name="a"},Work=new Person{Name="b"}});
        var idx=FindReferenceMarker(bytes); if(idx<0) return; bytes[idx]=1; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(idx+1), int.MaxValue);
        Assert.Throws<BinaryTypeException>(()=>new BinarySerializer(options).Deserialize<SharedReferenceGraph>(bytes));
    }

    [Fact]
    public void REF07_NegativeReferenceIdIsRejected()
    {
        var options = Preserve;
        var bytes = new BinarySerializer(options).Serialize(new Person { Name = "p" });
        
        const int rootMarkerOffset = 30;
        const int referenceIdOffset = 31;
        
        Assert.True(bytes.Length >= referenceIdOffset + 4);
        Assert.Equal(0, bytes[rootMarkerOffset]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(referenceIdOffset), -1);

        Assert.IsType<BinaryFormatException>(Record.Exception(() =>
            new BinarySerializer(options).Deserialize<Person>(bytes)));
    }

    [Fact] public void REF08_SharedPolymorphicInstancePreservesIdentity()
    {
        var dog=new Dog{Name="d",BarkVolume=2}; var value=new AnimalPair{A=dog,B=dog}; var actual=TestHelpers.RoundTrip(value,Preserve); Assert.IsType<Dog>(actual.A); Assert.Same(actual.A,actual.B);
    }

    [Fact] public void REF09_IdentityAcrossKeyedMembersIsRequired()
    {
        var dog=new Dog{Name="d"}; var value=new AnimalPairContract{A=dog,B=dog}; var actual=TestHelpers.RoundTrip(value,Preserve); Assert.Same(actual.A,actual.B);
    }

    private static int FindFirstObjectMarker(byte[] bytes) => 30;
    private static int FindReferenceMarker(byte[] bytes)
    {
        const int rootMarkerOffset = 30;
        for (int i = rootMarkerOffset; i < bytes.Length; i++)
        {
            if (bytes[i] is 0 or 1)
                return i;
        }

        return -1;
    }

    public sealed class AnimalPair { public Animal A {get;set;}=null!; public Animal B {get;set;}=null!; }
    [BinaryContract] public sealed class AnimalPairContract { [BinaryKey(1)] public Animal A {get;set;}=null!; [BinaryKey(2)] public Animal B {get;set;}=null!; }
}

public sealed class CircularReferenceTests
{
    [Fact] public void CYC01_DirectSelfReferenceIsCatchable() { var n=new CyclicNode{Name="self"}; n.Next=n; Assert.Throws<BinaryTypeException>(()=>new BinarySerializer().Serialize(n)); }
    [Fact] public void CYC02_TwoObjectCycleIsCatchable() { var a=new CyclicNode{Name="a"}; var b=new CyclicNode{Name="b"}; a.Next=b;b.Next=a;Assert.Throws<BinaryTypeException>(()=>new BinarySerializer().Serialize(a)); }
    [Fact]
    public void CYC03_CycleThroughCollectionIsCatchable()
    {
        var root = new CollectionCycleNode();
        root.Children.Add(root);
        Assert.Throws<BinaryTypeException>(() => new BinarySerializer().Serialize(root));
    }
    [Fact]
    public void CYC04_CycleThroughDictionaryIsCatchable()
    {
        var root = new DictionaryCycleNode();
        root.Children["self"] = root;
        Assert.Throws<BinaryTypeException>(() => new BinarySerializer().Serialize(root));
    }
    [Fact] public void CYC05_CycleThroughPolymorphicMemberIsCatchable() { var n=new PolyCycle(); n.Next=n; Assert.Throws<BinaryTypeException>(()=>new BinarySerializer().Serialize<PolyBase>(n)); }
    [Fact] public void CYC06_StructWrapperCycleIsCatchable() { var n=new StructWrapper(); n.Node=new CyclicNode(); n.Node.Next=n.Node; Assert.Throws<BinaryTypeException>(()=>new BinarySerializer().Serialize(n)); }
    [Fact] public void CYC07_SharedDagSucceeds() { var p=new Person{Name="p"}; var g=new SharedReferenceGraph{Home=p,Work=p}; Assert.NotEmpty(new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build()).Serialize(g)); }
    [Fact] public void CYC08_ReferenceDistinctEqualObjectsSucceed() { var g=new SharedReferenceGraph{Home=new Person{Name="x"},Work=new Person{Name="x"}}; Assert.NotEmpty(new BinarySerializer().Serialize(g)); }
    [Fact] public void CYC09_DeepChainBelowLimitSucceeds() { var head=new DeepNode(); var cur=head; for(int i=0;i<3;i++){cur.Next=new DeepNode();cur=cur.Next;} Assert.NotEmpty(new BinarySerializer(TestHelpers.TightOptions()).Serialize(head)); }
    [Fact] public void CYC10_ChainExactlyAtDepthBoundarySucceeds() { var head=new DeepNode(); var cur=head; for(int i=0;i<3;i++){cur.Next=new DeepNode();cur=cur.Next;} Assert.NotEmpty(new BinarySerializer(TestHelpers.TightOptions()).Serialize(head)); }
    [Fact] public void CYC11_ChainOneLevelAboveLimitFailsWithTypeExceptionOnWrite() { var head=new DeepNode(); var cur=head; for(int i=0;i<4;i++){cur.Next=new DeepNode();cur=cur.Next;} Assert.Throws<BinaryTypeException>(()=>new BinarySerializer(TestHelpers.TightOptions()).Serialize(head)); }
    [Fact] public void CYC11_ReadChainOneLevelAboveLimitFailsWithFormatException() { var head=new DeepNode(); var cur=head; for(int i=0;i<4;i++){cur.Next=new DeepNode();cur=cur.Next;} var bytes=new BinarySerializer().Serialize(head); Assert.Throws<BinaryFormatException>(()=>new BinarySerializer(TestHelpers.TightOptions()).Deserialize<DeepNode>(bytes)); }
    [Fact] public void CYC11_ChainFarAboveLimitAlsoFails() { var head=new DeepNode(); var cur=head; for(int i=0;i<5;i++){cur.Next=new DeepNode();cur=cur.Next;} Assert.Throws<BinaryTypeException>(()=>new BinarySerializer(TestHelpers.TightOptions()).Serialize(head)); }
    public abstract class PolyBase { public PolyBase? Next {get;set;} }
    public sealed class PolyCycle : PolyBase { }
    public sealed class StructWrapper { public CyclicNode? Node {get;set;} }
    public sealed class CollectionCycleNode { public List<CollectionCycleNode> Children { get; set; } = []; }
    public sealed class DictionaryCycleNode { public Dictionary<string, DictionaryCycleNode> Children { get; set; } = new(); }
}