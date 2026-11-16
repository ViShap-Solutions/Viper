using System.Buffers;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins WF-39: an array or a list of fixed-width numbers or characters is its count, then every element
/// little-endian — the same bytes as the elements written one by one, which a <see cref="Queue{T}"/>
/// of the same elements writes — and reads back under the same checks: a truncated payload is
/// malformed, the payload budget is a limit, and a dump still shows every element.
/// </summary>
public class ContiguousElementWireTests
{
    private static readonly int[] Lengths = [0, 1, 2, 126, 127, 128, 16_382, 16_383, 16_384];

    private readonly BinarySerializer _serializer = new();

    [Fact]
    public void Int32Array_IsTheCountThenEachElementLittleEndian()
    {
        foreach (int length in Lengths)
        {
            int[] values = Values(length, i => i * -0x01020304);

            byte[] expected = Wire.Payload(writer =>
            {
                writer.Write(Wire.Varint(length + 1));
                foreach (int value in values)
                    writer.Write(value);
            });

            Assert.Equal(expected, Wire.Body(_serializer.Serialize(values)));
            Assert.Equal(values, _serializer.Deserialize<int[]>(_serializer.Serialize(values)));
        }
    }

    [Fact]
    public void EveryFixedWidthElement_WritesTheBytesOfItsElementsWrittenOneByOne()
    {
        AssertAgrees(i => (byte)i);
        AssertAgrees(i => (sbyte)-i);
        AssertAgrees(i => (short)(i * -31));
        AssertAgrees(i => (ushort)(i * 977));
        AssertAgrees(i => (char)(0xD000 + i % 0x0FFF));
        AssertAgrees(i => i * -0x01020304);
        AssertAgrees(i => (uint)i * 0x9E3779B9u);
        AssertAgrees(i => i * -0x0102030405060708L);
        AssertAgrees(i => (ulong)i * 0x9E3779B97F4A7C15ul);
        AssertAgrees(i => (i % 4) switch
        {
            0 => -0.0f,
            1 => BitConverter.Int32BitsToSingle(0x7FC0_1234),
            2 => float.Epsilon,
            _ => i * 1.5f
        });
        AssertAgrees(i => (i % 4) switch
        {
            0 => -0.0,
            1 => BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_1234),
            2 => double.MaxValue,
            _ => i * -2.25
        });
    }

    [Fact]
    public void FixedWidthElements_AgreeUnderReferenceFramingAndInTheCompactCodec()
    {
        int[] values = Values(300, i => i * 7919);
        var queue = new Queue<int>(values);

        foreach (var options in new[]
                 {
                     BinarySerializerOptions.Configure().PreserveReferences().Build(),
                     BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build(),
                 })
        {
            var serializer = new BinarySerializer(options);
            Assert.Equal(serializer.Serialize(queue), serializer.Serialize(values));
            Assert.Equal(serializer.Serialize(queue), serializer.Serialize(new List<int>(values)));
            Assert.Equal(values, serializer.Deserialize<int[]>(serializer.Serialize(queue)));
        }
    }

    [Fact]
    public void NullAndEmptyFixedWidthArrays_KeepTheirEncodings()
    {
        Assert.Equal<byte[]>([0x00], Wire.Body(_serializer.Serialize<int[]?>(null)));
        Assert.Equal<byte[]>([0x01], Wire.Body(_serializer.Serialize(Array.Empty<double>())));
        Assert.Null(_serializer.Deserialize<int[]?>(_serializer.Serialize<int[]?>(null)));
        Assert.Empty(_serializer.Deserialize<double[]>(_serializer.Serialize(Array.Empty<double>()))!);
    }

    [Fact]
    public void TruncatedFixedWidthArray_IsMalformed()
    {
        byte[] body = Wire.Body(_serializer.Serialize(Values(1_000, i => i)));

        foreach (int cut in new[] { 1, 3, 4, 2_000, body.Length - 2 })
        {
            byte[] frame = Wire.Frame(body[..^cut]);
            Assert.Throws<BinaryFormatException>(() => _serializer.Deserialize<int[]>(frame));
        }
    }

    [Fact]
    public void FixedWidthArrayPastThePayloadBudget_IsALimitAndWritesNothing()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxPayloadBytes = 4_000 })
                .Build());
        var destination = new ArrayBufferWriter<byte>();

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(destination, Values(1_000, i => (long)i)));
        Assert.Equal(0, destination.WrittenCount);
        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(destination, Values(1_000, i => (long)i).ToList()));
        Assert.Equal(0, destination.WrittenCount);
    }

    [Fact]
    public void FixedWidthArrayOverTheArrayLimit_IsALimitOnBothSides()
    {
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxArrayLength = 10 })
                .Build());

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(Values(11, i => i)));
        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int[]>(_serializer.Serialize(Values(11, i => i))));
    }

    [Fact]
    public void DumpOfAFixedWidthArray_ShowsEveryElement()
    {
        double[] values = Values(40, i => i * 0.5);

        var dump = BinaryFormatDumper.Dump<double[]>(_serializer.Serialize(values));

        Assert.NotNull(dump.Root);
        Assert.Equal(values.Length, dump.Root!.Children.Count);
    }

    private void AssertAgrees<T>(Func<int, T> element)
    {
        foreach (int length in Lengths)
        {
            T[] values = Values(length, element);
            byte[] oneByOne = _serializer.Serialize(new Queue<T>(values));

            Assert.Equal(oneByOne, _serializer.Serialize(values));
            Assert.Equal(oneByOne, _serializer.Serialize(new List<T>(values)));
            Assert.Equal(values, _serializer.Deserialize<T[]>(oneByOne));
            Assert.Equal(values, _serializer.Deserialize<Queue<T>>(_serializer.Serialize(values))!);
        }
    }

    private static T[] Values<T>(int length, Func<int, T> element)
    {
        var values = new T[length];
        for (int i = 0; i < length; i++)
            values[i] = element(i);

        return values;
    }
}
