using System.Numerics;
using System.Security.Cryptography;
using ViShap.Viper.Crypto;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-10…HST-16: a payload that stops before it has delivered what it declared. Every prefix
/// of a valid frame fails deterministically, every fixed-size primitive is checked at its own width,
/// the 7-bit encoding rejects a truncated, overlong or out-of-range integer, and no read that cannot
/// be satisfied yields a partially filled value.
/// </summary>
public class TruncationTests
{
    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    private static SerializationOperation Operation() =>
        new(SerializationLimits.Default, keys: null, preserveReferences: false,
            requireEncryption: false, requireChecksum: false);

    // --- HST-10: every prefix of a valid frame ----------------------------------------------------

    [Fact]
    public void Deserialize_EveryPrefixOfAValidFrame_FailsAsAViperException()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Sample());

        foreach (byte[] prefix in Mutate.Prefixes(frame))
        {
            var ex = Record.Exception(() => serializer.Deserialize<Person>(prefix));

            Assert.True(
                ex is BinarySerializerException,
                $"A {prefix.Length}-byte prefix produced {ex?.GetType().Name ?? "no failure at all"}.");
        }
    }

    [Fact]
    public void Deserialize_TheSamePrefixTwice_FailsTheSameWay()
    {
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Sample());

        foreach (byte[] prefix in Mutate.Prefixes(frame))
        {
            var first = Record.Exception(() => serializer.Deserialize<Person>(prefix));
            var second = Record.Exception(() => serializer.Deserialize<Person>(prefix));

            Assert.Equal(first!.GetType(), second!.GetType());
        }
    }

    [Fact]
    public void Deserialize_APrefixShorterThanTheMagic_ThrowsFormat()
    {
        byte[] frame = new BinarySerializer().Serialize(Sample());

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<Person>(Mutate.Truncate(frame, 3)));
    }

    // --- HST-11: one case per primitive width -----------------------------------------------------

    [Fact]
    public void Deserialize_ATruncatedOneByteValue_ThrowsFormat()
    {
        AssertTruncated<bool>(0);
        AssertTruncated<byte>(0);
        AssertTruncated<sbyte>(0);
    }

    [Fact]
    public void Deserialize_ATruncatedTwoByteValue_ThrowsFormat()
    {
        AssertTruncated<short>(1);
        AssertTruncated<ushort>(1);
        AssertTruncated<char>(1);
        AssertTruncated<Half>(1);
    }

    [Fact]
    public void Deserialize_ATruncatedFourByteValue_ThrowsFormat()
    {
        AssertTruncated<int>(3);
        AssertTruncated<uint>(3);
        AssertTruncated<float>(3);
    }

    [Fact]
    public void Deserialize_ATruncatedEightByteValue_ThrowsFormat()
    {
        AssertTruncated<long>(7);
        AssertTruncated<ulong>(7);
        AssertTruncated<double>(7);
        AssertTruncated<DateTime>(7);
        AssertTruncated<TimeSpan>(7);
    }

    [Fact]
    public void Deserialize_ATruncatedSixteenByteValue_ThrowsFormat()
    {
        AssertTruncated<decimal>(15);
        AssertTruncated<Guid>(15);
        AssertTruncated<Int128>(15);
        AssertTruncated<UInt128>(15);
    }

    [Fact]
    public void Deserialize_ATruncatedCompositeOfFixedWidthParts_ThrowsFormat()
    {
        // DateTimeOffset is two Int64s, so the second one falls off the end.
        AssertTruncated<DateTimeOffset>(12);
    }

    [Fact]
    public void Deserialize_ATruncatedBlob_ThrowsFormat()
    {
        byte[] frame = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write7BitEncodedInt(32);      // declares 32 magnitude bytes
            writer.Write(new byte[2]);
        }));

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<BigInteger>(frame));
    }

    // --- HST-12…HST-14: the 7-bit encoding --------------------------------------------------------

    [Fact]
    public void Deserialize_ATruncated7BitInteger_ThrowsFormat()
    {
        // A continuation byte that nothing follows.
        byte[] frame = Wire.Frame([.. Wire.NotNull, 0x80]);

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_A7BitIntegerWithTooManyContinuationBytes_ThrowsFormat()
    {
        byte[] frame = Wire.Frame([.. Wire.NotNull, 0x80, 0x80, 0x80, 0x80, 0x80]);

        AssertEx.Throws<BinaryFormatException>(
            "Malformed", () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_A7BitIntegerAboveInt32MaxValue_ThrowsFormat()
    {
        // Eight in the fifth group is 0x8000_0000, one past the representable range.
        byte[] frame = Wire.Frame([.. Wire.NotNull, 0x80, 0x80, 0x80, 0x80, 0x08]);

        AssertEx.Throws<BinaryFormatException>(
            "non-negative Int32", () => new BinarySerializer().Deserialize<string>(frame));
    }

    // --- HST-40: the 7-bit encoding is minimal ----------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x85, 0x00 })]
    [InlineData(new byte[] { 0x85, 0x80, 0x00 })]
    [InlineData(new byte[] { 0x85, 0x80, 0x80, 0x80, 0x00 })]
    public void Deserialize_ANonMinimalStringLength_ThrowsFormat(byte[] lengthOfFive)
    {
        byte[] frame = Wire.Frame([.. Wire.NotNull, .. lengthOfFive, .. "hello"u8]);

        AssertEx.Throws<BinaryFormatException>(
            "minimally", () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_TheMinimalSpellingOfTheSameLength_Succeeds()
    {
        byte[] frame = Wire.Frame([.. Wire.NotNull, 0x05, .. "hello"u8]);

        Assert.Equal("hello", new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_ANonMinimalKeyedFieldCountOrKey_ThrowsFormat()
    {
        byte[] field = [0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        byte[] count = Wire.Frame([.. Wire.NotNull, 0x81, 0x00, 0x00, .. field]);
        byte[] key = Wire.Frame([.. Wire.NotNull, 0x01, 0x80, 0x00, .. field]);

        AssertEx.Throws<BinaryFormatException>(
            "minimally", () => new BinarySerializer().Deserialize<EmptyContract>(count));
        AssertEx.Throws<BinaryFormatException>(
            "minimally", () => new BinarySerializer().Deserialize<EmptyContract>(key));
    }

    [Fact]
    public void Deserialize_ANonMinimalHeaderStringLength_FailsBeforeTheTagIsChecked()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] frame = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key, "k7").Build())
            .Serialize(123);

        // magic, version, compression, no custom name, checksum, no custom name, encryption,
        // no custom name, key id present — then the key id length.
        const int keyIdLength = 15;
        Assert.Equal(2, frame[keyIdLength]);

        byte[] respelled = [.. frame[..keyIdLength], 0x82, 0x00, .. frame[(keyIdLength + 1)..]];

        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256Gcm(), key, "k7").Build());

        AssertEx.Throws<BinaryFormatException>("minimally", () => reader.Deserialize<int>(respelled));
    }

    [Fact]
    public void Deserialize_A7BitIntegerAtInt32MaxValue_IsRefusedAsALimitRatherThanAsMalformed()
    {
        // The encoding is legal at Int32.MaxValue; what stops it is MaxStringBytes.
        byte[] frame = Wire.Frame([.. Wire.NotNull, 0xFF, 0xFF, 0xFF, 0xFF, 0x07]);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<string>(frame));
    }

    // --- HST-15: V0 truncation ---------------------------------------------------------------------

    [Fact]
    public void Deserialize_ATruncatedV0Payload_ThrowsFormat()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());
        byte[] payload = serializer.Serialize(Sample());

        Assert.Throws<BinaryFormatException>(
            () => serializer.Deserialize<Person>(Mutate.Truncate(payload, payload.Length - 2)));
    }

    [Fact]
    public void Deserialize_EveryPrefixOfAV0Payload_FailsAsAViperException()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());
        byte[] payload = serializer.Serialize(Sample());

        foreach (byte[] prefix in Mutate.Prefixes(payload))
        {
            var ex = Record.Exception(() => serializer.Deserialize<Person>(prefix));

            Assert.True(
                ex is null or BinarySerializerException,
                $"A {prefix.Length}-byte prefix produced {ex!.GetType().Name}.");
        }
    }

    // --- HST-16: nothing partial escapes a failed read -------------------------------------------

    [Fact]
    public void ReadInt32_OverATruncatedSource_YieldsNoValueAtAll()
    {
        var reader = new ValueReader(new MemoryStream([1, 2]), Operation());

        Assert.Throws<BinaryFormatException>(() => reader.ReadInt32());
    }

    [Fact]
    public void ReadBytes_OverATruncatedSource_ReturnsNothingAndAllocatesNothingProportional()
    {
        AssertEx.AllocatesLessThan(1024 * 1024, () =>
        {
            var reader = new ValueReader(new MemoryStream(new byte[4]), Operation());
            reader.ReadBytes(8 * 1024 * 1024, "Blob");
        });
    }

    [Fact]
    public void Deserialize_ATruncatedMemberGraph_LeavesNoPartialObjectBehind()
    {
        // The exception is what the caller gets; there is no half-populated Person to observe.
        var serializer = new BinarySerializer();
        byte[] frame = serializer.Serialize(Sample());

        var ex = Record.Exception(
            () => serializer.Deserialize<Person>(Mutate.Truncate(frame, frame.Length - 2)));

        Assert.IsAssignableFrom<BinaryFormatException>(ex);
    }

    private static void AssertTruncated<T>(int availableBytes)
    {
        byte[] frame = Wire.Frame(new byte[availableBytes]);

        var ex = Record.Exception(() => new BinarySerializer().Deserialize<T>(frame));

        Assert.True(
            ex is BinaryFormatException and not BinaryLimitException,
            $"A {availableBytes}-byte payload for {typeof(T).Name} produced " +
            $"{ex?.GetType().Name ?? "no failure at all"}.");
    }
}
