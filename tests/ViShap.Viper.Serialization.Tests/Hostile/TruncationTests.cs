using System.Numerics;
using System.Security.Cryptography;
using ViShap.Viper.Crypto;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-10…HST-16, HST-40 and HST-41: a payload that stops before it has delivered what it
/// declared. Every prefix of a valid frame fails deterministically, every fixed-size primitive is
/// checked at its own width, the 7-bit encoding rejects a truncated, overlong, out-of-range or
/// non-minimal integer in every structural position, and no read that cannot be satisfied yields a
/// partially filled value.
/// </summary>
public class TruncationTests
{
    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    private static OperationBox Operation() => new();

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
        // A continuation byte that nothing follows, where the string's length begins.
        byte[] frame = Wire.Frame([0x80]);

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_A7BitIntegerWithTooManyContinuationBytes_ThrowsFormat()
    {
        byte[] frame = Wire.Frame([0x80, 0x80, 0x80, 0x80, 0x80]);

        AssertEx.Throws<BinaryFormatException>(
            "Malformed", () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_A7BitIntegerAboveInt32MaxValue_ThrowsFormat()
    {
        // Eight in the fifth group is 0x8000_0000, one past the representable range.
        byte[] frame = Wire.Frame([0x80, 0x80, 0x80, 0x80, 0x08]);

        AssertEx.Throws<BinaryFormatException>(
            "non-negative Int32", () => new BinarySerializer().Deserialize<string>(frame));
    }

    // --- HST-40: the 7-bit encoding is minimal ----------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x86, 0x00 })]
    [InlineData(new byte[] { 0x86, 0x80, 0x00 })]
    [InlineData(new byte[] { 0x86, 0x80, 0x80, 0x80, 0x00 })]
    public void Deserialize_ANonMinimalStringLength_ThrowsFormat(byte[] lengthOfFive)
    {
        // Five bytes, written as six because the length carries the string's null.
        byte[] frame = Wire.Frame([.. lengthOfFive, .. "hello"u8]);

        AssertEx.Throws<BinaryFormatException>(
            "minimally", () => new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_TheMinimalSpellingOfTheSameLength_Succeeds()
    {
        byte[] frame = Wire.Frame([0x06, .. "hello"u8]);

        Assert.Equal("hello", new BinarySerializer().Deserialize<string>(frame));
    }

    [Fact]
    public void Deserialize_ANonMinimalKeyedFieldCountOrKey_ThrowsFormat()
    {
        byte[] field = [0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        // One field, written as two because the count carries the object's null, then key 0.
        byte[] count = Wire.Frame([0x82, 0x00, 0x00, .. field]);
        byte[] key = Wire.Frame([0x02, 0x80, 0x00, .. field]);

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
            BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), key, "k7").Build())
            .Serialize(123);

        // The encryption record's body: AES-256-GCM, then the key id's length plus one, then "k7".
        var record = Wire.ReadHeader(frame).Service(Wire.EncryptionService);
        Assert.Equal<byte[]>([0x01, 0x03, 0x6B, 0x37], frame[record.BodyOffset..(record.BodyOffset + record.BodyLength)]);

        byte[] respelled = Wire.FrameWith(
            Wire.Body(frame),
            services: [Wire.Service(Wire.EncryptionService, critical: true, [0x01, 0x83, 0x00, 0x6B, 0x37])]);

        var reader = new BinarySerializer(
            BinarySerializerOptions.Configure().WithEncryption(new Aes256GcmEncryption(), key, "k7").Build());

        AssertEx.Throws<BinaryFormatException>("minimally", () => reader.Deserialize<int>(respelled));
    }

    [Fact]
    public void Deserialize_A7BitIntegerAtInt32MaxValue_IsRefusedAsALimitRatherThanAsMalformed()
    {
        // The encoding is legal at Int32.MaxValue; what stops it is MaxStringBytes.
        byte[] frame = Wire.Frame([0xFF, 0xFF, 0xFF, 0xFF, 0x07]);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<string>(frame));
    }

    // --- HST-41: every structural number is minimal ------------------------------------------------

    public static TheoryData<string, byte[]> NonMinimalStructuralNumbers()
    {
        byte[] magic = BitConverter.GetBytes(Wire.Magic);
        byte[] int42 = [42, 0, 0, 0];
        byte[] field = [0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        return new TheoryData<string, byte[]>
        {
            // A List<int> of one: its count, one plus one, spelled with a trailing zero group.
            { "count", Wire.Frame([0x82, 0x00, .. int42]) },
            // A string of five bytes: its length, five plus one.
            { "length", Wire.Frame([0x86, 0x00, .. "hello"u8]) },
            // A List<int> under references: the first occurrence of id 0.
            { "id", Wire.Frame([0x81, 0x00, 0x01, .. int42], preserveReferences: true) },
            // A keyed class of one field: key 0.
            { "key", Wire.Frame([0x02, 0x80, 0x00, .. field[1..]]) },
            { "version", [.. magic, 0x81, 0x00, 0x00, 0x00, 0x04, .. int42] },
            { "payload mode", [.. magic, 0x01, 0x80, 0x00, 0x00, 0x04, .. int42] },
            { "service kind", [.. magic, 0x01, 0x00, 0x01, 0x85, 0x00, 0x02, 0x01, 0x04, 0x04, .. int42] },
            { "service length", [.. magic, 0x01, 0x00, 0x01, 0x05, 0x82, 0x00, 0x01, 0x04, 0x04, .. int42] },
            { "onDiskLength", [.. magic, 0x01, 0x00, 0x00, 0x84, 0x00, .. int42] }
        };
    }

    [Theory]
    [MemberData(nameof(NonMinimalStructuralNumbers))]
    public void Deserialize_ANonMinimalStructuralNumber_ThrowsFormat(string position, byte[] frame)
    {
        var serializer = new BinarySerializer();

        Action read = position switch
        {
            "count" or "id" => () => serializer.Deserialize<List<int>>(frame),
            "length" => () => serializer.Deserialize<string>(frame),
            "key" => () => serializer.Deserialize<EmptyContract>(frame),
            _ => () => serializer.Deserialize<int>(frame)
        };

        AssertEx.Throws<BinaryFormatException>("minimally", read);
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
        Assert.Throws<BinaryFormatException>(() =>
        {
            var reader = new WireReader(new byte[] { 1, 2 }, ref Operation().State);
            reader.ReadInt32();
        });
    }

    [Fact]
    public void ReadBytes_OverATruncatedSource_ReturnsNothingAndAllocatesNothingProportional()
    {
        AssertEx.AllocatesLessThan(1024 * 1024, () =>
        {
            var reader = new WireReader(new byte[4], ref Operation().State);
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
