using System.Buffers;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-22: a span or a sequence read without a bytes-consumed form is exactly one frame, and
/// bytes after it are malformed; read with the form, the reader stops at the end of the frame — for
/// version 0, of the root value — and reports where, so frames placed back to back are read one after
/// another. The rule is the same for both formats.
/// </summary>
public class BytesConsumedTests
{
    public static TheoryData<string> Formats => ["V1", "V0"];

    private static BinarySerializer For(string format) => format == "V1"
        ? new BinarySerializer()
        : new BinarySerializer(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    private static (byte[] First, byte[] Second, byte[] Both) BackToBack(BinarySerializer serializer)
    {
        byte[] first = serializer.Serialize(new Person { Name = "Ada", Age = 36 });
        byte[] second = serializer.Serialize(new Person { Name = "Grace", Age = 85 });
        return (first, second, [.. first, .. second]);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Span_WithoutTheForm_RejectsTrailingBytes(string format)
    {
        var serializer = For(format);
        var (_, _, both) = BackToBack(serializer);

        AssertEx.Throws<BinaryFormatException>("after the end of the payload", () => serializer.Deserialize<Person>(both));
        Assert.Throws<BinaryFormatException>(() => serializer.Deserialize<Person>([.. serializer.Serialize(1), 0]));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Sequence_WithoutTheForm_RejectsTrailingBytes(string format)
    {
        var serializer = For(format);
        var (_, _, both) = BackToBack(serializer);

        AssertEx.Throws<BinaryFormatException>(
            "after the end of the payload", () => serializer.Deserialize<Person>(Sequences.Of(both[..5], both[5..])));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Span_WithTheForm_ReadsFramesBackToBackAndReportsWhereEachEnds(string format)
    {
        var serializer = For(format);
        var (first, second, both) = BackToBack(serializer);

        var one = serializer.Deserialize<Person>(both, out int firstConsumed);
        var two = serializer.Deserialize<Person>(both.AsSpan(firstConsumed), out int secondConsumed);

        Assert.Equal(first.Length, firstConsumed);
        Assert.Equal(second.Length, secondConsumed);
        Assert.Equal("Ada", one!.Name);
        Assert.Equal("Grace", two!.Name);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Sequence_WithTheForm_ReadsFramesBackToBackAndReportsThePositionAfterEach(string format)
    {
        var serializer = For(format);
        var (first, _, both) = BackToBack(serializer);
        var sequence = Sequences.Of(both[..7], both[7..(first.Length + 3)], both[(first.Length + 3)..]);

        var one = serializer.Deserialize<Person>(sequence, out SequencePosition afterFirst);
        var rest = sequence.Slice(afterFirst);
        var two = serializer.Deserialize<Person>(rest, out SequencePosition afterSecond);

        Assert.Equal(first.Length, sequence.Slice(sequence.Start, afterFirst).Length);
        Assert.Equal(sequence.End, afterSecond);
        Assert.Equal("Ada", one!.Name);
        Assert.Equal("Grace", two!.Name);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void TheForm_OverExactlyOneFrame_ReportsItsWholeLength(string format)
    {
        var serializer = For(format);
        byte[] frame = serializer.Serialize(new Person { Name = "Ada", Age = 36 });

        serializer.Deserialize<Person>(frame, out int bytesConsumed);
        serializer.Deserialize<Person>(Sequences.Of(frame), out SequencePosition consumed);

        Assert.Equal(frame.Length, bytesConsumed);
        Assert.Equal(frame.Length, Sequences.Of(frame).Slice(0, consumed).Length);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void TheForm_OverATruncatedFrame_ThrowsFormat(string format)
    {
        var serializer = For(format);
        byte[] frame = serializer.Serialize(new Person { Name = "Ada", Age = 36 });

        Assert.Throws<BinaryFormatException>(
            () => serializer.Deserialize<Person>(frame.AsSpan(0, frame.Length - 1), out _));
    }
}
