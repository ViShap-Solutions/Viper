using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Properties;

public sealed class ConfigurationMatrixTests
{
    public static IEnumerable<object[]> Configurations()
    {
        yield return new object[]{"CFG-01",BinarySerializerOptions.Default};
        yield return new object[]{"CFG-02",BinarySerializerOptions.Configure().WithCompression(new Deflate()).Build()};
        yield return new object[]{"CFG-03",BinarySerializerOptions.Configure().WithCompression(new Brotli()).Build()};
        yield return new object[]{"CFG-04",BinarySerializerOptions.Configure().WithChecksum(new Crc32()).Build()};
        yield return new object[]{"CFG-05",BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(),new byte[32],"k").Build()};
        yield return new object[]{"CFG-06",TestHelpers.FullPipeline(new byte[32])};
        yield return new object[]{"CFG-07",TestHelpers.FullPipeline(new byte[32],deflate:true)};
        yield return new object[]{"CFG-08",TestHelpers.FullPipeline(new byte[32],references:true)};
        yield return new object[]{"CFG-09",BinarySerializerOptions.Configure().WithLimits(TestHelpers.TightOptions().Limits).PreserveReferences().Build()};
        yield return new object[] {"CFG-10",TestHelpers.FullPipeline(new byte[32], references: true) with{ Limits = TestHelpers.TightOptions().Limits with { MaxMessageBytes = 64 } } };
        yield return new object[]{"CFG-11",BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build()};
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void ConfigurationSupportsFlatNestedAndCollectionShapes(string id,BinarySerializerOptions options)
    {
        var serializer = new BinarySerializer(options);
        
        if (id == "CFG-10")
        {
            var scalar = serializer.Deserialize<int>(
                serializer.Serialize(7));

            Assert.Equal(7, scalar);
            return;
        }

        var person = new Person { Name = "p", Age = 7, Address = new Address { City = "c", Street = "s" } };
        var actual = serializer.Deserialize<Person>(serializer.Serialize(person));
        Assert.Equal(person.Name, actual!.Name);
        Assert.Equal(person.Age, actual.Age);

        var list = new List<int> { 1, 2, 3 };
        var actualList = serializer.Deserialize<List<int>>(serializer.Serialize(list));
        Assert.Equal(list, actualList);

        if (options.Limits.MaxDepth >= 2)
        {
            var node = new DeepNode { Value = 1, Next = new DeepNode { Value = 2 } };
            var actualNode = serializer.Deserialize<DeepNode>(serializer.Serialize(node));
            Assert.Equal(2, actualNode!.Next!.Value);
        }
    }
}
