using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile;

/// <summary>
/// Pins HST-42…HST-45 and WF-38: a value whose body is a blob or a text has exactly one spelling on the
/// wire — the one its writer produces — so a reader refuses every other spelling of the same value, and
/// a writer refuses a string it could only write as a different one.
/// </summary>
public class CanonicalScalarTests
{
    private static readonly BinarySerializer Serializer = new();

    private static byte[] Blob(params byte[] bytes) => Wire.Frame([.. Wire.Varint(bytes.Length), .. bytes]);

    private static byte[] Text(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return Wire.Frame([.. Wire.Varint(bytes.Length + 1), .. bytes]);
    }

    // --- HST-42: BigInteger is its shortest two's complement ----------------------------------------

    [Fact]
    public void Deserialize_BigIntegerInItsShortestForm_IsRead()
    {
        Assert.Equal(BigInteger.One, Serializer.Deserialize<BigInteger>(Blob(0x01)));
        Assert.Equal(BigInteger.Zero, Serializer.Deserialize<BigInteger>(Blob(0x00)));
        Assert.Equal(new BigInteger(128), Serializer.Deserialize<BigInteger>(Blob(0x80, 0x00)));
        Assert.Equal(new BigInteger(-1), Serializer.Deserialize<BigInteger>(Blob(0xFF)));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x00, 0x00 })]
    [InlineData(new byte[0])]
    public void Deserialize_BigIntegerWithARedundantOrMissingByte_ThrowsFormat(byte[] body)
    {
        AssertEx.Throws<BinaryFormatException>("BigInteger", () => Serializer.Deserialize<BigInteger>(Blob(body)));
    }

    // --- HST-43: BitArray padding bits are zero -----------------------------------------------------

    [Fact]
    public void Deserialize_BitArrayWithASetPaddingBit_ThrowsFormat()
    {
        byte[] canonical = Wire.Frame([.. Wire.Varint(10), .. Wire.Varint(2), 0x8D, 0x01]);
        byte[] padded = Wire.Frame([.. Wire.Varint(10), .. Wire.Varint(2), 0x8D, 0xFF]);

        Assert.Equal(9, Serializer.Deserialize<BitArray>(canonical)!.Length);
        AssertEx.Throws<BinaryFormatException>("BitArray", () => Serializer.Deserialize<BitArray>(padded));
    }

    // --- HST-44: Version is its own ToString --------------------------------------------------------

    [Theory]
    [InlineData("01.2")]
    [InlineData(" 1.2")]
    [InlineData("1.2 ")]
    [InlineData("+1.2")]
    public void Deserialize_VersionInANonCanonicalSpelling_ThrowsFormat(string text)
    {
        Assert.Equal(new Version(1, 2), Serializer.Deserialize<Version>(Text("1.2")));
        AssertEx.Throws<BinaryFormatException>("Version", () => Serializer.Deserialize<Version>(Text(text)));
    }

    // --- HST-45: CultureInfo is its own Name --------------------------------------------------------

    [Fact]
    public void Deserialize_CultureNameThatIsNotTheCulturesOwnName_ThrowsFormat()
    {
        const string spelled = "EN-us";
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(spelled);
        }
        catch (CultureNotFoundException)
        {
            return;
        }

        Assert.Equal(culture, Serializer.Deserialize<CultureInfo>(Text(culture.Name)));

        if (culture.Name == spelled)
            return;

        AssertEx.Throws<BinaryFormatException>("canonical", () => Serializer.Deserialize<CultureInfo>(Text(spelled)));
    }

    // --- WF-38: a lone surrogate is refused on the way out ------------------------------------------

    public static TheoryData<int> LoneSurrogates => [0xD800, 0xDBFF, 0xDC00, 0xDFFF];

    [Theory]
    [MemberData(nameof(LoneSurrogates))]
    public void Serialize_AStringWithALoneSurrogate_ThrowsFormatAndWritesNothing(int surrogate)
    {
        string value = "a" + (char)surrogate + "b";
        var destination = new System.Buffers.ArrayBufferWriter<byte>();

        AssertEx.Throws<BinaryFormatException>("surrogate", () => Serializer.Serialize(destination, value));
        Assert.Equal(0, destination.WrittenCount);
    }

    [Fact]
    public void Serialize_AStringWithASurrogatePair_RoundTrips()
    {
        const string value = "😀";

        Assert.Equal(value, Serializer.Deserialize<string>(Serializer.Serialize(value)));
    }
}
