using System.Numerics;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins ENV-03, HST-11, HST-18 and HST-19: a payload that lies about its own shape is refused with a
/// format error, and a declaration larger than the bytes behind it never drives an allocation.
/// </summary>
public class MalformedPayloadTests
{
    private const long AllocationCeiling = 1024 * 1024;

    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    [Fact]
    public void Deserialize_PayloadWithTrailingBytes_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(123);
            writer.Write(456);
        }));

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_FrameDeclaringMoreThanTheWireBudget_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 64 });
        byte[] frame = Wire.Frame([], declaredLength: 8 * 1024 * 1024);

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_FrameDeclaringMoreThanTheWireBudget_AllocatesNothingProportional()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxWireBytes = 64 });
        byte[] frame = Wire.Frame([], declaredLength: 8 * 1024 * 1024);

        AssertEx.AllocatesLessThan(AllocationCeiling, () => serializer.Deserialize<int>(frame));
    }

    [Fact]
    public void Deserialize_StringLongerThanThePayload_ThrowsFormat()
    {
        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<string>(OversizedString()));
    }

    [Fact]
    public void Deserialize_StringLongerThanThePayload_AllocatesNothingProportional()
    {
        byte[] frame = OversizedString();

        AssertEx.AllocatesLessThan(
            AllocationCeiling, () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Deserialize_TruncatedGuid_ThrowsFormat(int availableBytes) =>
        AssertTruncatedValueThrowsFormat<Guid>(availableBytes);

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void Deserialize_TruncatedInt128_ThrowsFormat(int availableBytes) =>
        AssertTruncatedValueThrowsFormat<Int128>(availableBytes);

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void Deserialize_TruncatedUInt128_ThrowsFormat(int availableBytes) =>
        AssertTruncatedValueThrowsFormat<UInt128>(availableBytes);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Deserialize_TruncatedInt64_ThrowsFormat(int availableBytes) =>
        AssertTruncatedValueThrowsFormat<long>(availableBytes);

    [Fact]
    public void Deserialize_TruncatedBigInteger_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(32);      // declares 32 magnitude bytes
            writer.Write(new byte[2]);
        }));

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<BigInteger>(frame));
    }

    private static void AssertTruncatedValueThrowsFormat<T>(int availableBytes)
    {
        byte[] frame = Wire.Frame(new byte[availableBytes]);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<T>(frame));
    }

    private static byte[] OversizedString() => Wire.Frame(Wire.Payload(writer =>
    {
        writer.Write(true);                      // non-null string
        writer.Write7BitEncodedInt(3_000_000);   // declares far more than the frame holds
    }));
}
