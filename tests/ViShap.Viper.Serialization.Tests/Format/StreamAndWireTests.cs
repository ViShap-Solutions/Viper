namespace ViShap.Viper.Serialization.Tests.Format;

public sealed class WireAndFormatTests
{
    [Fact] public void V1HeaderHasMagicAndVersion()
    {
        var bytes=new BinarySerializer().Serialize(42); Assert.Equal(0x42,bytes[0]); Assert.Equal(0x53,bytes[1]); Assert.Equal(0x45,bytes[2]); Assert.Equal(0x52,bytes[3]); Assert.Equal(1,BitConverter.ToInt32(bytes,4));
    }
    [Fact] public void V0PayloadHasNoV1Header()
    {
        var o=BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(); var bytes=new BinarySerializer(o).Serialize(42); Assert.NotEqual(0x42,bytes[0]);
    }
    [Fact] public void V0ExplicitWriteReadIsStable()
    {
        var o=BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(); var s=new BinarySerializer(o); var bytes=s.Serialize(new Person{Name="v0",Age=1}); var actual=s.Deserialize<Person>(bytes); Assert.Equal("v0",actual!.Name); Assert.Equal(1,actual.Age);
    }
}
