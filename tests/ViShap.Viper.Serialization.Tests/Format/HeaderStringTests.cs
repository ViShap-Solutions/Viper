using System.Security.Cryptography;
using ViShap.Viper.Crypto;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins HDR-10: a V1 header string names an algorithm or selects a key, so its ceiling is fixed by
/// the format rather than borrowed from the payload's string policy.
/// </summary>
public class HeaderStringTests
{
    private const int MaxHeaderStringBytes = 256;

    private static BinarySerializer Encrypted(string keyId) =>
        new(BinarySerializerOptions.Configure()
            .WithEncryption(new Aes256Gcm(), RandomNumberGenerator.GetBytes(32), keyId)
            .Build());

    [Fact]
    public void Serialize_KeyIdAtTheCeiling_RoundTrips()
    {
        var serializer = Encrypted(new string('k', MaxHeaderStringBytes));

        byte[] payload = serializer.Serialize(new Person { Name = "Ada", Age = 36 });

        Assert.Equal("Ada", serializer.Deserialize<Person>(payload)!.Name);
    }

    [Fact]
    public void Serialize_KeyIdAboveTheCeiling_ThrowsConfiguration()
    {
        var serializer = Encrypted(new string('k', MaxHeaderStringBytes + 1));

        Assert.Throws<BinaryConfigurationException>(
            () => serializer.Serialize(new Person { Name = "Ada", Age = 36 }));
    }

    [Fact]
    public void Serialize_KeyIdWhoseEncodedLengthExceedsTheCeiling_ThrowsConfiguration()
    {
        // The ceiling counts UTF-8 bytes, not characters.
        var serializer = Encrypted(new string('ж', MaxHeaderStringBytes / 2 + 1));

        Assert.Throws<BinaryConfigurationException>(
            () => serializer.Serialize(new Person { Name = "Ada", Age = 36 }));
    }

    [Theory]
    [InlineData(MaxHeaderStringBytes + 1)]
    [InlineData(1_000)]
    [InlineData(100_000)]
    public void Deserialize_HeaderStringAboveTheCeiling_IsRefusedByTheCeiling(int declaredLength)
    {
        // The bytes are all present, so only the ceiling can reject this.
        byte[] frame = Wire.FrameWithOversizedCustomName(declaredLength, actualBytes: declaredLength);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));

        Assert.Contains($"admits at most {MaxHeaderStringBytes}", ex.Message);
    }

    [Fact]
    public void Deserialize_HeaderStringAboveTheConfiguredStringLimit_IsStillAFormatError()
    {
        // The payload's string policy does not govern the header: this is malformed, not over-budget.
        byte[] frame = Wire.FrameWithOversizedCustomName(
            declaredLength: SerializationLimits.Default.MaxStringBytes + 1, actualBytes: 0);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int>(frame));

        Assert.Contains($"admits at most {MaxHeaderStringBytes}", ex.Message);
    }

    [Fact]
    public void Deserialize_HeaderStringDeclaredAtTheCeilingButTruncated_ThrowsFormat()
    {
        byte[] frame = Wire.FrameWithOversizedCustomName(
            declaredLength: MaxHeaderStringBytes, actualBytes: 1);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(frame));
    }
}
