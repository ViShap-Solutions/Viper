using System.IO.Pipelines;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-24: <c>Populate</c> reads a payload into a class the caller already holds. Only the root
/// is populated, a keyed contract keeps what the payload does not mention, and a payload that is not a
/// member layout of that class is refused rather than reinterpreted.
/// </summary>
public class PopulateTests
{
    private readonly BinarySerializer _serializer = new();

    private static BinarySerializer WithReferences() =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    private byte[] Alice => _serializer.Serialize(new Person { Name = "Alice", Age = 30 });

    private static void AssertIsAlice(Person person)
    {
        Assert.Equal("Alice", person.Name);
        Assert.Equal(30, person.Age);
    }

    // --- every source populates the instance it was given ----------------------------------------

    [Fact]
    public void Populate_Span_FillsTheInstanceItWasGiven()
    {
        var target = new Person();

        _serializer.Populate(Alice, target);

        AssertIsAlice(target);
    }

    [Fact]
    public void Populate_Sequence_FillsTheInstanceItWasGiven()
    {
        byte[] payload = Alice;
        var target = new Person();

        _serializer.Populate(Sequences.Of(payload[..3], payload[3..20], payload[20..]), target);

        AssertIsAlice(target);
    }

    [Fact]
    public void Populate_Stream_FillsTheInstanceItWasGiven()
    {
        using var stream = new MemoryStream(Alice, writable: false);
        var target = new Person();

        _serializer.Populate(stream, target);

        AssertIsAlice(target);
    }

    [Fact]
    public async Task PopulateAsync_Stream_FillsTheInstanceItWasGiven()
    {
        using var stream = new MemoryStream(Alice, writable: false);
        var target = new Person();

        await _serializer.PopulateAsync(stream, target);

        AssertIsAlice(target);
    }

    [Fact]
    public async Task PopulateAsync_Pipe_FillsTheInstanceItWasGiven()
    {
        var pipe = new ChunkedPipeReader(Alice, chunkSize: 5);
        var target = new Person();

        await _serializer.PopulateAsync(pipe, target);

        AssertIsAlice(target);
        Assert.Equal(Alice.Length, pipe.Consumed);
    }

    [Fact]
    public void Populate_TheSameInstanceTwice_OverwritesEveryMember()
    {
        var target = new Person();

        _serializer.Populate(Alice, target);
        _serializer.Populate(_serializer.Serialize(new Person { Name = "Bob", Age = 41 }), target);

        Assert.Equal("Bob", target.Name);
        Assert.Equal(41, target.Age);
    }

    // --- only the root -----------------------------------------------------------------------------

    [Fact]
    public void Populate_NestedObjects_AreCreatedAfresh()
    {
        var existingChild = new Node { Value = 1 };
        var target = new CompleteContract { Child = existingChild };
        byte[] payload = _serializer.Serialize(new CompleteContract { Number = 7, Child = new Node { Value = 9 } });

        _serializer.Populate(payload, target);

        Assert.Equal(7, target.Number);
        Assert.NotSame(existingChild, target.Child);
        Assert.Equal(9, target.Child!.Value);
        Assert.Equal(1, existingChild.Value);
    }

    [Fact]
    public void Populate_KeyedContract_KeepsAFieldThePayloadDoesNotCarry()
    {
        // OuterKeys knows keys 1 and 3; a payload written with a schema that only had key 1 leaves
        // key 3 where the instance already had it.
        byte[] payload = Wire.Frame([.. Wire.NotNull, .. Wire.KeyedBody([new Wire.KeyedField(1, BitConverter.GetBytes(5))])]);
        var target = new OuterKeys { First = 1, Last = 99 };

        _serializer.Populate(payload, target);

        Assert.Equal(5, target.First);
        Assert.Equal(99, target.Last);
    }

    [Fact]
    public void Populate_UnionOfTheSameRuntimeType_FillsTheInstance()
    {
        byte[] payload = _serializer.Serialize<UnionBase>(new UnionDerived { Z = 3, A = 4 });
        UnionBase target = new UnionDerived();

        _serializer.Populate(payload, target);

        Assert.Equal(3, target.Z);
        Assert.Equal(4, ((UnionDerived)target).A);
    }

    // --- what is refused -------------------------------------------------------------------------

    [Fact]
    public void Populate_TypeWithADedicatedFormatter_ThrowsType()
    {
        // A collection's payload is not a member layout, so it must not be reinterpreted as one.
        byte[] payload = _serializer.Serialize(new List<int> { 1, 2, 3 });

        Assert.Throws<BinaryTypeException>(() => _serializer.Populate(payload, new List<int>()));
    }

    [Fact]
    public void Populate_UnionOfAnotherRuntimeType_ThrowsType()
    {
        byte[] payload = _serializer.Serialize<UnionBase>(new UnionDerived { Z = 3, A = 4 });

        Assert.Throws<BinaryTypeException>(() => _serializer.Populate(payload, new UnionBase()));
    }

    [Fact]
    public void Populate_NullRoot_ThrowsFormat()
    {
        byte[] payload = _serializer.Serialize<Person?>(null);

        AssertEx.Throws<BinaryFormatException>("null root", () => _serializer.Populate(payload, new Person()));
    }

    [Fact]
    public void Populate_RootThatIsABackReference_ThrowsFormat()
    {
        byte[] payload = Wire.Frame([.. Wire.NotNull, .. Wire.ReferenceFrame(1, 0)], preserveReferences: true);

        AssertEx.Throws<BinaryFormatException>(
            "back reference", () => WithReferences().Populate(payload, new Person()));
    }

    [Fact]
    public void Populate_WithPreserveReferences_FillsTheInstance()
    {
        var serializer = WithReferences();
        var target = new Person();

        serializer.Populate(serializer.Serialize(new Person { Name = "Alice", Age = 30 }), target);

        AssertIsAlice(target);
    }

    [Fact]
    public void Populate_EmptyInput_ThrowsFormatAndLeavesTheTargetUntouched()
    {
        var target = new Person { Name = "Ada", Age = 36 };

        Assert.Throws<BinaryFormatException>(() => _serializer.Populate([], target));

        Assert.Equal("Ada", target.Name);
        Assert.Equal(36, target.Age);
    }

    [Fact]
    public void Populate_NullTarget_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => _serializer.Populate(Alice, (Person)null!));
        Assert.Throws<ArgumentNullException>(() => _serializer.Populate(Sequences.Of(Alice), (Person)null!));
        Assert.Throws<ArgumentNullException>(() => _serializer.Populate(new MemoryStream(Alice), (Person)null!));
        Assert.Throws<ArgumentNullException>(() => _serializer.Populate((Stream)null!, new Person()));
        Assert.Throws<ArgumentNullException>(
            () => _serializer.PopulateAsync(new MemoryStream(Alice), (Person)null!).AsTask().GetAwaiter().GetResult());
        Assert.Throws<ArgumentNullException>(
            () => _serializer.PopulateAsync((PipeReader)null!, new Person()).AsTask().GetAwaiter().GetResult());
    }

    // --- the bytes-consumed forms ----------------------------------------------------------------

    [Fact]
    public void Populate_SpanWithBytesConsumed_StopsAtTheEndOfTheFrame()
    {
        byte[] first = Alice;
        byte[] second = _serializer.Serialize(new Person { Name = "Bob", Age = 41 });
        byte[] both = [.. first, .. second];
        var target = new Person();

        _serializer.Populate(both, target, out int bytesConsumed);

        Assert.Equal(first.Length, bytesConsumed);
        AssertIsAlice(target);

        _serializer.Populate(both.AsSpan(bytesConsumed), target, out int secondConsumed);
        Assert.Equal(second.Length, secondConsumed);
        Assert.Equal("Bob", target.Name);
    }

    [Fact]
    public void Populate_SequenceWithConsumed_ReportsThePositionAfterTheFrame()
    {
        byte[] first = Alice;
        byte[] both = [.. first, .. first];
        var sequence = Sequences.Of(both[..10], both[10..40], both[40..]);
        var target = new Person();

        _serializer.Populate(sequence, target, out SequencePosition consumed);

        Assert.Equal(first.Length, sequence.Slice(sequence.Start, consumed).Length);
        AssertIsAlice(target);
    }

    [Fact]
    public void Populate_WithoutBytesConsumed_RejectsTrailingBytes()
    {
        byte[] both = [.. Alice, .. Alice];

        Assert.Throws<BinaryFormatException>(() => _serializer.Populate(both, new Person()));
        Assert.Throws<BinaryFormatException>(() => _serializer.Populate(Sequences.Of(both), new Person()));
    }
}
