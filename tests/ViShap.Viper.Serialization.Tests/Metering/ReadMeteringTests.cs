using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Metering;

/// <summary>
/// Pins STR-01…STR-08: a read takes the source's bytes into memory within the operation's budget,
/// counted from where the operation starts; a <see cref="WireReader"/> over them knows exactly how
/// many remain, and classifies a declaration it cannot satisfy by which bound it broke — the budget
/// the bytes were cut to, or the end of the bytes themselves.
/// </summary>
public class ReadMeteringTests
{
    private static OperationBox Operation() => new();

    /// <summary>A reader over <paramref name="physicalBytes"/> bytes cut to <paramref name="budget"/>, as a pipeline builds one.</summary>
    private static long Remaining(int physicalBytes, long budget)
    {
        byte[] bytes = new byte[physicalBytes];
        var reader = new WireReader(
            bytes.AsSpan(0, (int)Math.Min(physicalBytes, budget)), ref Operation().State, new WireBudget("test", budget));
        return reader.Remaining;
    }

    private static BinarySerializer V0(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().WithLimits(limits).Build());

    private static Person Sample() => new() { Name = "Alice", Age = 30 };

    // --- STR-01: the count starts where the operation does --------------------------------------

    [Fact]
    public void Deserialize_V1FromAStreamAtANonZeroOffset_SpendsTheWireBudgetFromZero()
    {
        byte[] frame = new BinarySerializer().Serialize(Sample());
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxWireBytes = frame.Length })
                .Build());
        using var source = new MemoryStream([.. new byte[40], .. frame]) { Position = 40 };

        var restored = serializer.Deserialize<Person>(source);

        Assert.Equal("Alice", restored!.Name);
        Assert.Equal(40 + frame.Length, source.Position);
    }

    [Fact]
    public void Deserialize_V0FromAStreamAtANonZeroOffset_SpendsThePayloadBudgetFromZero()
    {
        byte[] payload = V0(SerializationLimits.Default).Serialize(Sample());
        var serializer = V0(SerializationLimits.Default with { MaxPayloadBytes = payload.Length });
        using var source = new MemoryStream([.. new byte[40], .. payload, .. new byte[40]]) { Position = 40 };

        var restored = serializer.Deserialize<Person>(source);

        Assert.Equal(30, restored!.Age);
        Assert.Equal(40 + payload.Length, source.Position);
    }

    // --- STR-02: under and at the budget --------------------------------------------------------

    [Fact]
    public void Read_UnderTheBudget_Succeeds()
    {
        var reader = new WireReader(new byte[10], ref Operation().State, new WireBudget("test", 10));

        reader.ReadExact(new byte[9], "Blob");

        Assert.Equal(9, reader.Consumed);
    }

    [Fact]
    public void Read_AtExactlyTheBudget_Succeeds()
    {
        var reader = new WireReader(new byte[10], ref Operation().State, new WireBudget("test", 10));

        reader.ReadExact(new byte[10], "Blob");

        Assert.Equal(10, reader.Consumed);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void Read_OneByteBeyondTheBudget_ThrowsLimit()
    {
        Assert.Throws<BinaryLimitException>(() =>
        {
            var reader = new WireReader(new byte[10], ref Operation().State, new WireBudget("test", 10));
            reader.ReadExact(new byte[10], "Blob");
            reader.ReadByte();
        });
    }

    [Fact]
    public void Deserialize_V0PayloadAtExactlyThePayloadBudget_Succeeds()
    {
        byte[] payload = V0(SerializationLimits.Default).Serialize(Sample());

        var restored = V0(SerializationLimits.Default with { MaxPayloadBytes = payload.Length })
            .Deserialize<Person>(payload);

        Assert.Equal("Alice", restored!.Name);
    }

    [Fact]
    public void Deserialize_V0PayloadOneByteOverThePayloadBudget_ThrowsLimit()
    {
        byte[] payload = V0(SerializationLimits.Default).Serialize(Sample());

        Assert.Throws<BinaryLimitException>(
            () => V0(SerializationLimits.Default with { MaxPayloadBytes = payload.Length - 1 })
                .Deserialize<Person>(payload));
    }

    // --- STR-03 / STR-04: what a declaration may claim, and how a failure is classified ---------

    [Fact]
    public void Remaining_WhenTheBudgetExceedsTheBytes_IsTheBytes()
    {
        Assert.Equal(10, Remaining(physicalBytes: 10, budget: 1_000));
    }

    [Fact]
    public void Remaining_WhenTheBytesExceedTheBudget_IsTheBudget()
    {
        Assert.Equal(10, Remaining(physicalBytes: 1_000, budget: 10));
    }

    [Fact]
    public void Remaining_AfterReading_Shrinks()
    {
        var reader = new WireReader(new byte[10], ref Operation().State, new WireBudget("test", 1_000));

        reader.ReadExact(new byte[4], "Blob");

        Assert.Equal(6, reader.Remaining);
    }

    [Fact]
    public void RequireAvailable_BeyondTheBudget_IsALimitViolation()
    {
        byte[] bytes = new byte[1_000];

        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(bytes.AsSpan(0, 8), ref Operation().State, new WireBudget("test", 8));
            reader.RequireAvailable(20, "Payload");
        });

        Assert.IsType<BinaryLimitException>(ex);
    }

    [Fact]
    public void RequireAvailable_WithinTheBudgetButBeyondTheBytes_IsAMalformedPayload()
    {
        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(new byte[10], ref Operation().State, new WireBudget("test", 1_000));
            reader.RequireAvailable(20, "Payload");
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void RequireAvailable_WithoutABudget_IsAMalformedPayload()
    {
        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(new byte[10], ref Operation().State);
            reader.RequireAvailable(20, "Payload");
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void Deserialize_V1FrameDeclaringMoreThanTheWireBudget_ThrowsLimit()
    {
        byte[] frame = new BinarySerializer().Serialize(Sample());
        var serializer = new BinarySerializer(
            BinarySerializerOptions.Configure()
                .WithLimits(SerializationLimits.Default with { MaxWireBytes = frame.Length - 1 })
                .Build());

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<Person>(frame));
    }

    [Fact]
    public void Deserialize_V1FrameShorterThanItDeclares_ThrowsFormat()
    {
        byte[] frame = new BinarySerializer().Serialize(Sample());

        var ex = Record.Exception(
            () => new BinarySerializer().Deserialize<Person>(Mutate.Truncate(frame, frame.Length - 1)));

        Assert.IsType<BinaryFormatException>(ex);
    }

    // --- STR-05: an underlying I/O failure keeps its cause --------------------------------------

    [Fact]
    public void Deserialize_FromAFailingSource_ThrowsStreamExceptionPreservingTheCause()
    {
        using var source = new FailingStream(bytesBeforeFailure: 2);

        var ex = Assert.Throws<BinaryStreamException>(
            () => new BinarySerializer().Deserialize<int>(source));

        Assert.IsType<IOException>(ex.InnerException);
    }

    // --- STR-06: the caller's stream is never disposed -------------------------------------------

    [Fact]
    public void Deserialize_FromACallerStream_LeavesItOpen()
    {
        using var source = new TrackingStream(new BinarySerializer().Serialize(123));

        Assert.Equal(123, new BinarySerializer().Deserialize<int>(source));

        Assert.False(source.Disposed);
    }

    [Fact]
    public void Deserialize_FromACallerStreamThatFails_LeavesItOpen()
    {
        using var source = new TrackingStream([1, 2, 3]);

        Assert.Throws<BinaryFormatException>(() => new BinarySerializer().Deserialize<int>(source));

        Assert.False(source.Disposed);
    }

    // --- STR-07: V0 applies both the wire and the payload ceiling ------------------------------

    [Fact]
    public void Deserialize_V0_WhenTheWireBudgetIsTighter_StopsAtTheWireBudget()
    {
        byte[] payload = V0(SerializationLimits.Default).Serialize(new string('x', 64));
        var serializer = V0(SerializationLimits.Default with { MaxWireBytes = 20, MaxPayloadBytes = 1_000 });

        AssertEx.Throws<BinaryLimitException>("wire", () => serializer.Deserialize<string>(payload));
    }

    [Fact]
    public void Deserialize_V0_WhenThePayloadBudgetIsTighter_StopsAtThePayloadBudget()
    {
        byte[] payload = V0(SerializationLimits.Default).Serialize(new string('x', 64));
        var serializer = V0(SerializationLimits.Default with { MaxWireBytes = 1_000, MaxPayloadBytes = 20 });

        AssertEx.Throws<BinaryLimitException>("payload", () => serializer.Deserialize<string>(payload));
    }

    // --- STR-08: a source that answers in small pieces ------------------------------------------

    [Fact]
    public void Deserialize_V1FromAPartialReadSource_RoundTrips()
    {
        byte[] frame = new BinarySerializer().Serialize(
            new Person { Name = "Alice in a name long enough to span several chunks", Age = 42 });
        using var source = new PartialReadStream(frame, chunkSize: 3);

        var restored = new BinarySerializer().Deserialize<Person>(source);

        Assert.Equal("Alice in a name long enough to span several chunks", restored!.Name);
        Assert.Equal(42, restored.Age);
    }

    [Fact]
    public void Deserialize_V0FromAPartialReadSource_RoundTrips()
    {
        var serializer = V0(SerializationLimits.Default);
        byte[] payload = serializer.Serialize(
            new Person { Name = "Alice in a name long enough to span several chunks", Age = 42 });
        using var source = new PartialReadStream(payload, chunkSize: 5);

        var restored = serializer.Deserialize<Person>(source);

        Assert.Equal(42, restored!.Age);
        Assert.Equal(payload.Length, source.Position);
    }
}
