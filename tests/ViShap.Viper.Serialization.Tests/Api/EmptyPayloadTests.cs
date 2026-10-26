using System.Buffers;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-21: no wire version encodes a value in zero bytes, not even a null root, so an empty span,
/// sequence, stream or pipe is <see cref="BinaryFormatException"/> at every read and populate entry
/// point, and a populate target is left untouched. A <see langword="null"/> array converts to an empty
/// span, so it is refused the same way.
/// </summary>
public class EmptyPayloadTests
{
    private static readonly BinarySerializer Serializer = new();

    private static readonly BinarySerializer Headerless = new(
        BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    public static TheoryData<string> Serializers => ["V1", "V0 fallback"];

    private static BinarySerializer For(string name) => name == "V1" ? Serializer : Headerless;

    [Theory]
    [MemberData(nameof(Serializers))]
    public void Deserialize_EmptySpan_ThrowsFormat(string serializer)
    {
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<Person>([]));
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<int>([]));
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<Person>([], out _));
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void Deserialize_EmptySequence_ThrowsFormat(string serializer)
    {
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<Person>(ReadOnlySequence<byte>.Empty));
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<Person>(Sequences.Of<byte>([], []), out _));
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public void Deserialize_EmptyStream_ThrowsFormat(string serializer)
    {
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<Person>(new MemoryStream()));
        Assert.Throws<BinaryFormatException>(() => For(serializer).Deserialize<Person>(new NonSeekableStream([])));
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task DeserializeAsync_EmptyStreamOrPipe_ThrowsFormat(string serializer)
    {
        await Assert.ThrowsAsync<BinaryFormatException>(
            () => For(serializer).DeserializeAsync<Person>(new NonSeekableStream([])).AsTask());
        await Assert.ThrowsAsync<BinaryFormatException>(
            () => For(serializer).DeserializeAsync<Person>(new ChunkedPipeReader([], chunkSize: 4)).AsTask());
    }

    [Fact]
    public void Deserialize_NullArray_ThrowsFormat()
    {
        // A null array converts to an empty span; the empty span is what is refused.
        Assert.Throws<BinaryFormatException>(() => Serializer.Deserialize<Person>((byte[])null!));
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task Populate_EmptyInput_ThrowsFormatAndLeavesTheTargetUntouched(string serializer)
    {
        var person = new Person { Name = "Ada", Age = 36 };
        var instance = For(serializer);

        Assert.Throws<BinaryFormatException>(() => instance.Populate([], person));
        Assert.Throws<BinaryFormatException>(() => instance.Populate([], person, out _));
        Assert.Throws<BinaryFormatException>(() => instance.Populate(ReadOnlySequence<byte>.Empty, person));
        Assert.Throws<BinaryFormatException>(() => instance.Populate(ReadOnlySequence<byte>.Empty, person, out _));
        Assert.Throws<BinaryFormatException>(() => instance.Populate(new MemoryStream(), person));
        Assert.Throws<BinaryFormatException>(() => instance.Populate((byte[])null!, person));
        await Assert.ThrowsAsync<BinaryFormatException>(
            () => instance.PopulateAsync(new MemoryStream(), person).AsTask());
        await Assert.ThrowsAsync<BinaryFormatException>(
            () => instance.PopulateAsync(new ChunkedPipeReader([], chunkSize: 4), person).AsTask());

        Assert.Equal("Ada", person.Name);
        Assert.Equal(36, person.Age);
    }
}
