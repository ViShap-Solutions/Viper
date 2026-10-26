using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-28: the frame is copied into a caller's buffer writer in as many spans as it hands out,
/// however short, and a writer that hands out an empty span — which its contract forbids — is a
/// failed destination, reported as <see cref="BinaryStreamException"/>, rather than a copy that never
/// ends.
/// </summary>
public class BufferWriterTests
{
    private static Person Value => new() { Name = new string('a', 300), Age = 36 };

    public static TheoryData<int> Versions => [1, 0];

    private static BinarySerializer For(int version) =>
        new(BinarySerializerOptions.Configure().WithVersion(version).Build());

    [Theory]
    [MemberData(nameof(Versions))]
    public void Serialize_IntoAWriterHandingOutShortSpans_WritesTheBytesOfTheArrayForm(int version)
    {
        var serializer = For(version);
        var destination = new StingyBufferWriter(limit: 3);

        serializer.Serialize(destination, Value);

        Assert.Equal(serializer.Serialize(Value), destination.Written);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Serialize_IntoAWriterHandingOutAnEmptySpan_ThrowsStreamInsteadOfLoopingForever(int version)
    {
        var serializer = For(version);
        var destination = new StingyBufferWriter(limit: 0);

        // Run apart, so a copy that never ends fails this test instead of hanging the suite.
        var write = Task.Run(() => serializer.Serialize(destination, Value));
        bool ended = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(10))) == write;

        Assert.True(ended, "The write into a writer that hands out no space never ended.");
        var thrown = Assert.IsType<AggregateException>(write.Exception).InnerException;
        Assert.IsType<BinaryStreamException>(thrown);
        Assert.Empty(destination.Written);
    }
}
