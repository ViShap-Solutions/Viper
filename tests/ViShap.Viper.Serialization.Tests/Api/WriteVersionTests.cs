using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins OPT-10 and V0-11: the write version is configuration, validated once when options are built,
/// and selecting it is independent of the read-side headerless fallback.
/// </summary>
public class WriteVersionTests
{
    private static BinarySerializer V0Writer(bool allowFallback = false) =>
        new(BinarySerializerOptions.Configure()
            .WithVersion(0)
            .AllowV0Fallback(allowFallback)
            .Build());

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Build_UnsupportedWriteVersion_ThrowsConfiguration(int version)
    {
        var builder = BinarySerializerOptions.Configure().WithVersion(version);

        var ex = Assert.Throws<BinaryConfigurationException>(() => builder.Build());

        Assert.Contains(version.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Build_SupportedWriteVersion_Succeeds(int version)
    {
        var options = BinarySerializerOptions.Configure().WithVersion(version).Build();

        Assert.Equal(version, options.WriteVersion);
    }

    [Fact]
    public void Serialize_V0WithoutTheReadFallback_WritesTheHeaderlessPayload()
    {
        byte[] payload = V0Writer().Serialize(new Person { Name = "Ada", Age = 36 });

        Assert.NotEmpty(payload);
        Assert.NotEqual(Wire.Magic, BitConverter.ToInt32(payload));
    }

    [Fact]
    public void Deserialize_V0PayloadWithoutTheReadFallback_ThrowsFormat()
    {
        // Writing V0 is a write-side choice; reading a headerless stream stays opt-in.
        byte[] payload = V0Writer().Serialize(new Person { Name = "Ada", Age = 36 });

        Assert.Throws<BinaryFormatException>(() => V0Writer().Deserialize<Person>(payload));
    }

    [Fact]
    public void Deserialize_V0PayloadWithTheReadFallback_RoundTrips()
    {
        var serializer = V0Writer(allowFallback: true);

        byte[] payload = serializer.Serialize(new Person { Name = "Ada", Age = 36 });
        var restored = serializer.Deserialize<Person>(payload);

        Assert.Equal("Ada", restored!.Name);
        Assert.Equal(36, restored.Age);
    }
}
