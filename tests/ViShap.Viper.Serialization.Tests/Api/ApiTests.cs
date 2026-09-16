using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

public sealed class PublicApiTests
{
    [Fact] public void API01_DefaultConstructorWorks()
    {
        var serializer = new BinarySerializer();
        Assert.Equal(42, serializer.Deserialize<int>(serializer.Serialize(42)));
    }

    [Fact] public void API03_DefaultOptionsAreCanonical()
    {
        Assert.Same(BinarySerializerOptions.Default, BinarySerializerOptions.Default);
        Assert.Equal(BinarySerializerOptions.Default.WriteVersion, BinarySerializerOptions.Configure().Build().WriteVersion);
    }

    [Fact] public void API04_BuilderDefaultsEquivalentToDefaultForRepresentativeValue()
    {
        var value = new Person { Name="Ada", Age=37, Address=new Address{City="London",Street="Main"} };
        Assert.Equal(new BinarySerializer().Serialize(value), new BinarySerializer(BinarySerializerOptions.Configure().Build()).Serialize(value));
    }

    [Fact] public void API05_RecordWithWorksFromExternalAssembly()
    {
        var options = BinarySerializerOptions.Default with { PreserveReferences = true };
        Assert.True(options.PreserveReferences);
        Assert.False(BinarySerializerOptions.Default.PreserveReferences);
    }

    [Fact] public void API06_LimitsBuilder()
    {
        var limits = TestHelpers.TightOptions().Limits;
        Assert.Equal(4, limits.MaxDepth); Assert.Equal(3, limits.MaxArrayLength); Assert.Equal(32, limits.MaxMessageBytes);
    }

    [Fact] public void API07_AlgorithmBuilder()
    {
        var options = TestHelpers.FullPipeline(new byte[32]);
        var value = new Person{Name="x",Age=1,Address=new Address{City="y",Street="z"}};
        var actual = new BinarySerializer(options).Deserialize<Person>(new BinarySerializer(options).Serialize(value));
        Assert.Equal(value.Name, actual!.Name);
    }

    [Fact] public void API09_PreserveReferencesBuilder()
    {
        var shared = new Person{Name="shared"}; var graph = new SharedReferenceGraph{Home=shared,Work=shared};
        var actual = new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build()).Deserialize<SharedReferenceGraph>(new BinarySerializer(BinarySerializerOptions.Configure().PreserveReferences().Build()).Serialize(graph));
        Assert.Same(actual!.Home, actual.Work);
    }

    [Fact] public void API10_V0WriterAndFallback()
    {
        var options = BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build();
        var serializer = new BinarySerializer(options); var bytes = serializer.Serialize(new Person{Name="v0"});
        Assert.Equal("v0", serializer.Deserialize<Person>(bytes)!.Name);
        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<Person>(bytes));
    }

    [Fact] public void API11_StreamOwnership()
    {
        var stream = new MemoryStream(); new BinarySerializer().Serialize(stream, 42); Assert.True(stream.CanWrite); stream.Position=0; Assert.Equal(42, new BinarySerializer().Deserialize<int>(stream)); Assert.True(stream.CanRead);
    }

    [Fact] public void API12_NullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Deserialize<int>((byte[])null!));
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Deserialize<int>((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Deserialize((byte[])null!, new Person()));
        Assert.Throws<ArgumentNullException>(() => new BinarySerializer().Deserialize((Stream)null!, new Person()));
    }

    [Fact] public void ByteArrayEmptyHasDocumentedDefaultSemantics()
    {
        Assert.Equal(0, new BinarySerializer().Deserialize<int>(Array.Empty<byte>()));
        var existing = new Person{Name="before"};
        Assert.Null(new BinarySerializer().Deserialize(Array.Empty<byte>(), existing));
        Assert.Equal("before", existing.Name);
    }
}

public sealed class StreamExtensionApiTests
{
    [Fact] public void StandardAndOptionsOverloads()
    {
        using var s = new MemoryStream(); s.Serialize(123); s.Position=0; Assert.Equal(123, s.Deserialize<int>());
        using var s2 = new MemoryStream(); s2.Serialize(456, BinarySerializerOptions.Default); s2.Position=0; Assert.Equal(456, s2.Deserialize<int>(BinarySerializerOptions.Default));
    }

    [Fact] public void KeyAndResolverOverloadsWithEncryptedPayload()
    {
        var key = new byte[32]; var options = BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key, "k").Build();
        using var stream = new MemoryStream(); new BinarySerializer(options).Serialize(stream, 777); stream.Position=0;
        Assert.Equal(777, stream.Deserialize<int>(key));
        stream.Position=0; Assert.Equal(777, stream.Deserialize<int>(_ => key));
    }

    [Fact] public void ExistingReferenceAndValueRefOverloads()
    {
        var serializer = new BinarySerializer(); var person = new Person{Name="new",Age=9}; var bytes=serializer.Serialize(person);
        using var stream=new MemoryStream(bytes); var existing=new Person{Name="old"}; var result=stream.Deserialize(existing); Assert.Same(existing,result); Assert.Equal("new",result!.Name);
        using var stream2=new MemoryStream(serializer.Serialize(new PointStruct(4,5))); PointStruct point=default; stream2.Deserialize(ref point); Assert.Equal(new PointStruct(4,5),point);
    }
    [Fact]
    public void AllExistingInstanceKeyAndOptionsOverloadsAreUsable()
    {
        var key=new byte[32]; var options=BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),key,"k").Build(); var serializer=new BinarySerializer(options);
        var bytes=serializer.Serialize(new Person{Name="encrypted",Age=10});
        using(var a=new MemoryStream(bytes)){var existing=new Person();Assert.Same(existing,a.Deserialize(existing,options));}
        using(var b=new MemoryStream(bytes)){var existing=new Person();Assert.Same(existing,b.Deserialize(existing,key));}
        using(var c=new MemoryStream(bytes)){var existing=new Person();Assert.Same(existing,c.Deserialize(existing,_=>key));}
        using(var d=new MemoryStream(serializer.Serialize(new PointStruct(1,2)))){PointStruct p=default;d.Deserialize(ref p,options);Assert.Equal(new PointStruct(1,2),p);}
        using(var e=new MemoryStream(serializer.Serialize(new PointStruct(3,4)))){PointStruct p=default;e.Deserialize(ref p,key);Assert.Equal(new PointStruct(3,4),p);}
        using(var f=new MemoryStream(serializer.Serialize(new PointStruct(5,6)))){PointStruct p=default;f.Deserialize(ref p,_=>key);Assert.Equal(new PointStruct(5,6),p);}
    }

}
