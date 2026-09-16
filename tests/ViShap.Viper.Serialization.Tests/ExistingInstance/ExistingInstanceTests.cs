namespace ViShap.Viper.Serialization.Tests.ExistingInstance;

public sealed class ExistingInstanceTests
{
    [Fact] public void EXI01_ReferenceInstanceIsReused()
    {
        var serializer=new BinarySerializer(); var bytes=serializer.Serialize(new Person{Name="new",Age=2}); var existing=new Person{Name="old",Age=1}; var result=serializer.Deserialize(bytes,existing); Assert.Same(existing,result); Assert.Equal("new",existing.Name); Assert.Equal(2,existing.Age);
    }
    [Fact] public void EXI02_SequentialReuseFullyOverwritesMembers()
    {
        var serializer=new BinarySerializer(); var existing=new Person{Name="old",Age=99}; var r1=serializer.Deserialize(serializer.Serialize(new Person{Name="one",Age=1}),existing); var r2=serializer.Deserialize(serializer.Serialize(new Person{Name="two",Age=2}),existing); Assert.Same(existing,r1); Assert.Same(existing,r2); Assert.Equal("two",existing.Name); Assert.Equal(2,existing.Age);
    }
    [Fact] public void EXI03_EmptyByteArrayDoesNotMutateExisting()
    {
        var existing=new Person{Name="before"}; Assert.Null(new BinarySerializer().Deserialize(Array.Empty<byte>(),existing)); Assert.Equal("before",existing.Name);
    }
    [Fact] public void EXI04_ExistingBaseAgainstSerializedDerivedFails()
    {
        var options=BinarySerializerOptions.Configure().Build(); var serializer=new BinarySerializer(options); var bytes=serializer.Serialize<Animal>(new Dog{Name="d"}); Animal existing = new Cat { Name = "c" }; Assert.Throws<BinaryTypeException>(()=>serializer.Deserialize(bytes,existing));
    }
    [Fact] public void EXI05_RootReferenceMarkerCannotBeUsedAsExistingRoot()
    {
        var options=BinarySerializerOptions.Configure().PreserveReferences().Build(); var serializer=new BinarySerializer(options); var bytes=serializer.Serialize(new Person{Name="p"}); const int rootMarkerOffset = 30; bytes[rootMarkerOffset] = 1; Assert.Throws<BinaryTypeException>(()=>serializer.Deserialize(bytes,new Person()));
    }
    [Fact] public void EXI06_ValueTypeRefReceivesAllValues()
    {
        var serializer=new BinarySerializer(); PointStruct point=new(0,0); serializer.Deserialize(serializer.Serialize(new PointStruct(7,8)),ref point); Assert.Equal(new PointStruct(7,8),point);
    }
    [Fact] public void EXI07_StreamAndByteArrayParity()
    {
        var serializer=new BinarySerializer(); var value=new Person{Name="p",Age=5}; var bytes=serializer.Serialize(value); var fromBytes=serializer.Deserialize<Person>(bytes); using var stream=new MemoryStream(bytes); var fromStream=serializer.Deserialize<Person>(stream); Assert.Equal(fromBytes!.Name,fromStream!.Name); Assert.Equal(fromBytes.Age,fromStream.Age);
    }
    [Fact] public void EXI08_FullPipelinePreservesIdentity()
    {
        var key=new byte[32]; var options=TestHelpers.FullPipeline(key); var serializer=new BinarySerializer(options); var existing=new Person(); var bytes=serializer.Serialize(new Person{Name="pipeline",Age=8}); using var stream=new MemoryStream(bytes); var actual=serializer.Deserialize(stream,existing); Assert.Same(existing,actual); Assert.Equal("pipeline",actual!.Name);
    }
}