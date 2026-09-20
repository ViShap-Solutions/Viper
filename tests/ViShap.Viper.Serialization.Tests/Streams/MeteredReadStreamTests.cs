using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Streams;

/// <summary>
/// Pins STR-01…STR-08 and D1: the read meter counts what this operation consumes, relative to where
/// it started; it reports what a declared length may actually claim — the lesser of the remaining
/// budget and the bytes the source can still deliver — and it classifies a declaration it cannot
/// satisfy by which of the two it broke.
/// </summary>
public class MeteredReadStreamTests
{
    private static MeteredReadStream Over(int physicalBytes, long budget) =>
        new(new MemoryStream(new byte[physicalBytes]), budget, "test");

    // --- STR-01: the count starts where the operation does --------------------------------------

    [Fact]
    public void Position_OverAStreamAtANonZeroOffset_StartsAtZero()
    {
        var source = new MemoryStream(new byte[100]) { Position = 40 };

        var stream = new MeteredReadStream(source, 1_000, "test");

        Assert.Equal(0, stream.Position);
        Assert.Equal(0, stream.BytesRead);
    }

    [Fact]
    public void RemainingBytes_OverAStreamAtANonZeroOffset_CountsOnlyWhatIsLeft()
    {
        var source = new MemoryStream(new byte[100]) { Position = 40 };

        var stream = new MeteredReadStream(source, 1_000, "test");

        Assert.Equal(60, stream.RemainingBytes);
    }

    [Fact]
    public void Read_OverAStreamAtANonZeroOffset_SpendsTheBudgetFromZero()
    {
        // The caller's 40 pre-existing bytes cost this operation nothing, so a budget of 10 still
        // admits 10 bytes.
        var source = new MemoryStream(new byte[100]) { Position = 40 };
        var stream = new MeteredReadStream(source, 10, "test");

        stream.ReadExactly(new byte[10]);

        Assert.Equal(10, stream.BytesRead);
        Assert.Equal(50, source.Position);
    }

    // --- STR-02: under and at the budget --------------------------------------------------------

    [Fact]
    public void Read_UnderTheBudget_Succeeds()
    {
        var stream = Over(physicalBytes: 100, budget: 10);

        stream.ReadExactly(new byte[9]);

        Assert.Equal(9, stream.BytesRead);
    }

    [Fact]
    public void Read_AtExactlyTheBudget_Succeeds()
    {
        var stream = Over(physicalBytes: 100, budget: 10);

        stream.ReadExactly(new byte[10]);

        Assert.Equal(10, stream.BytesRead);
        Assert.Equal(0, stream.RemainingBytes);
    }

    [Fact]
    public void Read_OneByteBeyondTheBudget_ThrowsLimit()
    {
        var stream = Over(physicalBytes: 100, budget: 10);
        stream.ReadExactly(new byte[10]);

        Assert.Throws<BinaryLimitException>(() => stream.ReadByte());
    }

    // --- D1-04: RemainingBytes never exceeds the physical remainder ----------------------------

    [Fact]
    public void RemainingBytes_WhenBudgetExceedsTheSource_ReportsThePhysicalRemainder()
    {
        var stream = Over(physicalBytes: 10, budget: 1_000);

        Assert.Equal(10, stream.RemainingBytes);
    }

    [Fact]
    public void RemainingBytes_WhenTheSourceExceedsTheBudget_ReportsTheBudget()
    {
        var stream = Over(physicalBytes: 1_000, budget: 10);

        Assert.Equal(10, stream.RemainingBytes);
    }

    [Fact]
    public void RemainingBytes_AfterReading_ShrinksWithTheSource()
    {
        var stream = Over(physicalBytes: 10, budget: 1_000);

        stream.ReadExactly(new byte[4]);

        Assert.Equal(6, stream.RemainingBytes);
    }

    [Fact]
    public void RemainingBytes_OverANestedMeter_FollowsThePhysicalTruth()
    {
        var wire = Over(physicalBytes: 10, budget: 1_000);
        var payload = new MeteredReadStream(wire, 1_000, "payload");

        Assert.Equal(10, payload.RemainingBytes);
    }

    // --- STR-07: nesting applies both ceilings independently ------------------------------------

    [Fact]
    public void RemainingBytes_OverANestedMeter_ReportsTheTighterOfTheTwoBudgets()
    {
        var wire = Over(physicalBytes: 1_000, budget: 100);
        var payload = new MeteredReadStream(wire, 20, "payload");

        Assert.Equal(20, payload.RemainingBytes);
    }

    [Fact]
    public void Read_OverANestedMeter_ChargesBothBudgets()
    {
        var wire = Over(physicalBytes: 1_000, budget: 100);
        var payload = new MeteredReadStream(wire, 60, "payload");

        payload.ReadExactly(new byte[40]);

        Assert.Equal(40, payload.BytesRead);
        Assert.Equal(40, wire.BytesRead);
        Assert.Equal(20, payload.RemainingBytes);
        Assert.Equal(60, wire.RemainingBytes);
    }

    [Fact]
    public void Read_OverANestedMeter_StopsAtTheInnerBudgetFirst()
    {
        var wire = Over(physicalBytes: 1_000, budget: 100);
        var payload = new MeteredReadStream(wire, 20, "payload");

        AssertEx.Throws<BinaryLimitException>("payload", () => payload.Read(new byte[40]));
    }

    [Fact]
    public void Read_OverANestedMeter_StopsAtTheOuterBudgetWhenItIsTighter()
    {
        var wire = new MeteredReadStream(new MemoryStream(new byte[1_000]), 20, "wire");
        var payload = new MeteredReadStream(wire, 100, "payload");

        AssertEx.Throws<BinaryLimitException>("wire", () => payload.Read(new byte[40]));
    }

    // --- D1-05: the two failures stay distinct -------------------------------------------------

    [Fact]
    public void Exceeded_BeyondTheBudget_IsALimitViolation()
    {
        var stream = Over(physicalBytes: 1_000, budget: 8);

        var ex = stream.Exceeded(20, "Payload");

        Assert.IsType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Exceeded_WithinTheBudgetButBeyondTheSource_IsAMalformedPayload()
    {
        var stream = Over(physicalBytes: 10, budget: 1_000);

        var ex = stream.Exceeded(20, "Payload");

        Assert.IsType<BinaryFormatException>(ex);
    }

    [Fact]
    public void Read_BeyondTheBudget_ThrowsLimit()
    {
        var stream = Over(physicalBytes: 1_000, budget: 8);

        Assert.Throws<BinaryLimitException>(() => stream.Read(new byte[20]));
    }

    [Fact]
    public void Read_BeyondTheSourceButWithinTheBudget_ReturnsShortInsteadOfThrowing()
    {
        // Truncation is discovered by the caller that asked for the bytes, not reclassified here as
        // a limit violation.
        var stream = Over(physicalBytes: 10, budget: 1_000);

        int read = stream.Read(new byte[20]);

        Assert.Equal(10, read);
    }

    // --- STR-05: an underlying I/O failure keeps its cause --------------------------------------

    [Fact]
    public void Read_WhenTheSourceFails_ThrowsStreamExceptionPreservingTheCause()
    {
        using var source = new FailingStream(bytesBeforeFailure: 4);
        var stream = new MeteredReadStream(source, 1_000, "test");

        var ex = Assert.Throws<BinaryStreamException>(() => stream.Read(new byte[8]));

        Assert.IsType<IOException>(ex.InnerException);
    }

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
    public void Dispose_LeavesTheCallerStreamOpen()
    {
        using var source = new TrackingStream(new byte[16]);

        using (var stream = new MeteredReadStream(source, 1_000, "test"))
            stream.ReadExactly(new byte[4]);

        Assert.False(source.Disposed);
    }

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

    // --- STR-08: a source that answers in small pieces ------------------------------------------

    [Fact]
    public void Read_FromAPartialReadSource_LosesNoData()
    {
        byte[] content = [.. Enumerable.Range(0, 64).Select(value => (byte)value)];
        using var source = new PartialReadStream(content, chunkSize: 7);
        var stream = new MeteredReadStream(source, 1_000, "test");

        byte[] destination = new byte[64];
        stream.ReadExactly(destination);

        Assert.Equal(content, destination);
        Assert.Equal(64, stream.BytesRead);
    }

    [Fact]
    public void Deserialize_FromAPartialReadSource_RoundTrips()
    {
        byte[] frame = new BinarySerializer().Serialize(
            new Person { Name = "Alice in a name long enough to span several chunks", Age = 42 });
        using var source = new PartialReadStream(frame, chunkSize: 3);

        var restored = new BinarySerializer().Deserialize<Person>(source);

        Assert.Equal("Alice in a name long enough to span several chunks", restored!.Name);
        Assert.Equal(42, restored.Age);
    }
}
