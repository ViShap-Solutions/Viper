using System.Runtime.ExceptionServices;
using ViShap.Viper.Io;

namespace ViShap.Viper.Serialization.Tests.Metering;

/// <summary>
/// Pins STR-31…STR-35: the write buffers an operation rents are kept per thread between operations, and
/// keeping them changes nothing an operation can observe — the memory handed out is cleared, whatever the
/// previous operation wrote or failed to commit; a buffer goes back once however often it is disposed;
/// the budget is the new operation's; and a buffer disposed on another thread is kept there. Each test
/// runs on a thread of its own, so no other test's buffers are in its cache.
/// </summary>
public class WriteBufferReuseTests
{
    private static T OnThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);

        return result;
    }

    [Fact]
    public void Rent_AfterAWriteWasDisposed_HandsOutClearedMemory()
    {
        var (first, second, handedOut) = OnThread(() =>
        {
            var buffer = PayloadBuffer.Rent(1 << 20, "payload");
            var span = buffer.GetSpan(64);
            span[..64].Fill(0xAB);
            buffer.Advance(64);
            buffer.Dispose();

            var reused = PayloadBuffer.Rent(1 << 20, "payload");
            return (buffer, reused, reused.GetSpan(1).ToArray());
        });

        Assert.Same(first, second);
        Assert.All(handedOut, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Rent_AfterAWriteThatWasNeverCommitted_HandsOutClearedMemory()
    {
        var (first, second, handedOut) = OnThread(() =>
        {
            var buffer = PayloadBuffer.Rent(1 << 20, "payload");
            buffer.GetSpan(64).Fill(0xCD);
            buffer.Dispose();

            var reused = PayloadBuffer.Rent(1 << 20, "payload");
            return (buffer, reused, reused.GetSpan(1).ToArray());
        });

        Assert.Same(first, second);
        Assert.All(handedOut, value => Assert.Equal(0, value));
    }

    [Fact]
    public void Dispose_Twice_ReturnsTheBufferOnce()
    {
        var (first, second) = OnThread(() =>
        {
            var buffer = PayloadBuffer.Rent(1 << 20, "payload");
            buffer.Dispose();
            buffer.Dispose();

            return (PayloadBuffer.Rent(1 << 20, "payload"), PayloadBuffer.Rent(1 << 20, "payload"));
        });

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Rent_AReusedBuffer_EnforcesTheNewOperationsBudget()
    {
        var failure = OnThread(() =>
        {
            PayloadBuffer.Rent(1 << 20, "payload").Dispose();

            var reused = PayloadBuffer.Rent(8, "wire");
            return Record.Exception(() => reused.GetSpan(9));
        });

        var limit = Assert.IsType<BinaryLimitException>(failure);
        Assert.Contains("wire", limit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_OnAnotherThread_KeepsTheBufferOnThatThread()
    {
        var (rented, rentedThere, rentedHere) = OnThread(() =>
        {
            var buffer = PayloadBuffer.Rent(1 << 20, "payload");
            var there = OnThread(() =>
            {
                buffer.Dispose();
                return PayloadBuffer.Rent(1 << 20, "payload");
            });

            return (buffer, there, PayloadBuffer.Rent(1 << 20, "payload"));
        });

        Assert.Same(rented, rentedThere);
        Assert.NotSame(rented, rentedHere);
    }
}
