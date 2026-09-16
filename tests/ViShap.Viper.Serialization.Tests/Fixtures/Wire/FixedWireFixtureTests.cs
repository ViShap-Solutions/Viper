namespace ViShap.Viper.Serialization.Tests.Fixtures.Wire;

public sealed class FixedWireFixtureTests
{
    [Fact] public void V0_Int32_42_IsStable() { var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","Wire","v0-int32-42.bin")); var options=BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(); Assert.Equal(42,new BinarySerializer(options).Deserialize<int>(bytes)); }
    [Fact] public void V1_Int32_42_IsStable() { var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","Wire","v1-int32-42.bin")); Assert.Equal(42,new BinarySerializer().Deserialize<int>(bytes)); }
    [Fact] public void V1_FixtureHasExpectedHeaderBytes() { var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","Wire","v1-int32-42.bin")); Assert.Equal(new byte[]{0x42,0x53,0x45,0x52,1,0,0,0,0,0,0,0,0,0,0,0,0,4,0,0,0,4,0,0,0,4,0,0,0},bytes[..29]); }
}
