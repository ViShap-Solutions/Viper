using System.Buffers;
using System.Text;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins WF-40: a string of the payload is its UTF-8 byte length plus one, then the bytes, at every
/// length — across the lengths where that number, or three bytes a character, needs another byte —
/// for one string and for many in a row; a lone surrogate is malformed and a string past
/// <c>MaxStringBytes</c> a limit, and neither leaves anything in the destination.
/// </summary>
public class StringEncodingWireTests
{
    private static readonly int[] Lengths =
        [0, 1, 2, 41, 42, 43, 44, 63, 64, 126, 127, 128, 129, 5_460, 5_461, 5_462, 16_382, 16_383, 16_384, 16_385, 100_000];

    private static readonly string[] Units = ["a", "é", "中", "😀", "a中", "é😀"];

    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void String_IsItsUtf8LengthPlusOneThenTheBytes_AtEveryLength()
    {
        foreach (string unit in Units)
        {
            foreach (int length in Lengths)
            {
                string value = Repeat(unit, length);

                Assert.Equal(Expected(value), Wire.Body(_serializer.Serialize(value)));
                Assert.Equal(value, _serializer.Deserialize<string>(_serializer.Serialize(value)));

                byte[] inList = Wire.Body(_serializer.Serialize(new List<string> { value }));
                Assert.Equal([0x02, .. Expected(value)], inList);
            }
        }
    }

    [Fact]
    public void ManyStringsInARow_AreEachTheirLengthThenTheirBytes()
    {
        var random = new Random(0x5717);
        var values = new List<string?>();
        for (int i = 0; i < 4_000; i++)
        {
            values.Add(i % 97 == 0
                ? null
                : Repeat(Units[random.Next(Units.Length)], random.Next(0, i % 50 == 0 ? 3_000 : 60)));
        }

        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(Wire.Varint(values.Count + 1));
            foreach (string? value in values)
                writer.Write(value is null ? [0x00] : Expected(value));
        });

        Assert.Equal(expected, Wire.Body(_serializer.Serialize(values)));
        Assert.Equal(values, _serializer.Deserialize<List<string?>>(_serializer.Serialize(values)));
    }

    [Fact]
    public void LoneSurrogate_IsMalformedAndWritesNothing()
    {
        var destination = new ArrayBufferWriter<byte>();

        foreach (string value in new[] { "\uD800", "ab\uDC00", "x\uD83D", Repeat("a", 60) + "\uDFFF" + "b" })
        {
            var thrown = Assert.Throws<BinaryFormatException>(() => _serializer.Serialize(destination, value));
            Assert.IsType<EncoderFallbackException>(thrown.InnerException);

            Assert.Throws<BinaryFormatException>(() => _serializer.Serialize(destination, new List<string> { "ok", value }));
        }

        Assert.Equal(0, destination.WrittenCount);
    }

    [Fact]
    public void StringPastMaxStringBytes_IsALimitAndWritesNothing()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxStringBytes = 10 })
                .Build());
        var destination = new ArrayBufferWriter<byte>();

        foreach (string fits in new[] { Repeat("a", 10), Repeat("é", 5), Repeat("中", 3) + "a" })
        {
            Assert.Equal(Expected(fits), Wire.Body(serializer.Serialize(fits)));
            Assert.Equal([0x02, .. Expected(fits)], Wire.Body(serializer.Serialize(new List<string> { fits })));
        }

        foreach (string over in new[] { Repeat("a", 11), Repeat("é", 5) + "a", Repeat("中", 4) })
        {
            Assert.Throws<BinaryLimitException>(() => serializer.Serialize(destination, over));
            Assert.Throws<BinaryLimitException>(() => serializer.Serialize(destination, new List<string> { "a", over }));
        }

        Assert.Equal(0, destination.WrittenCount);
    }

    private static byte[] Expected(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return [.. Wire.Varint(bytes.Length + 1), .. bytes];
    }

    private static string Repeat(string unit, int count) => string.Concat(Enumerable.Repeat(unit, count));
}
