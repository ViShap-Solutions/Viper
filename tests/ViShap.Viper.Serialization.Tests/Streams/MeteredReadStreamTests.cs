using ViShap.Viper.Security;

namespace ViShap.Viper.Serialization.Tests.Streams;

/// <summary>
/// Pins D1: a metered source reports what a declared length may actually claim — the lesser of the
/// operation's remaining budget and the bytes the source can still deliver — and classifies a
/// declaration it cannot satisfy by which of the two it broke.
/// </summary>
public class MeteredReadStreamTests
{
    private static MeteredReadStream Over(int physicalBytes, long budget) =>
        new(new MemoryStream(new byte[physicalBytes]), budget, "test");

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
}
