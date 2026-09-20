namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Assertions the plan asks for in more than one suite. Anything here is shared on purpose: an
/// assertion written twice drifts, and a drifting assertion weakens the gate it belongs to.
/// </summary>
internal static class AssertEx
{
    /// <summary>
    /// Asserts the exact exception type and that its message names the rule that fired.
    /// <c>Assert.Throws</c> is already exact-type, so only the substring is added here.
    /// </summary>
    public static TException Throws<TException>(string messageSubstring, Action act)
        where TException : Exception
    {
        var exception = Assert.Throws<TException>(act);
        Assert.Contains(messageSubstring, exception.Message, StringComparison.Ordinal);
        return exception;
    }

    /// <summary>
    /// Asserts that <paramref name="act"/> allocates less than <paramref name="ceiling"/> bytes,
    /// which is how a test proves a declaration was refused rather than honoured and then abandoned.
    /// </summary>
    /// <remarks>
    /// The action runs once first so that JIT and per-type cache allocations are not attributed to
    /// the measurement. Viper exceptions are expected and swallowed; anything else propagates.
    /// </remarks>
    public static void AllocatesLessThan(long ceiling, Action act)
    {
        Warm(act);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Warm(act);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < ceiling,
            $"Expected fewer than {ceiling:N0} bytes, but {allocated:N0} were allocated.");

        static void Warm(Action act)
        {
            try
            {
                act();
            }
            catch (BinarySerializerException)
            {
            }
        }
    }

    /// <summary>
    /// Asserts that <paramref name="needle"/> appears nowhere in <paramref name="haystack"/>, which
    /// is how a test shows that a name or a value never reached the wire.
    /// </summary>
    public static void DoesNotContainBytes(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        int at = haystack.AsSpan().IndexOf(needle);

        Assert.True(at < 0, $"Expected the bytes to be absent, but they start at offset {at}.");
    }

    /// <summary>Asserts that two sequences hold the same elements, whatever their order.</summary>
    public static void SameContents<T>(IEnumerable<T> expected, IEnumerable<T>? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(
            expected.OrderBy(item => item, Comparer<T>.Default).ToArray(),
            actual.OrderBy(item => item, Comparer<T>.Default).ToArray());
    }

    /// <summary>Asserts a stack's contents by popping it, rather than by trusting enumeration.</summary>
    public static void PopsInOrder<T>(IEnumerable<T> expected, Stack<T>? actual)
    {
        Assert.NotNull(actual);
        foreach (var item in expected)
            Assert.Equal(item, actual.Pop());

        Assert.Empty(actual);
    }

    /// <summary>Asserts a queue's contents by dequeuing it.</summary>
    public static void DequeuesInOrder<T>(IEnumerable<T> expected, Queue<T>? actual)
    {
        Assert.NotNull(actual);
        foreach (var item in expected)
            Assert.Equal(item, actual.Dequeue());

        Assert.Empty(actual);
    }

    /// <summary>
    /// Asserts a priority queue by dequeuing it, which is the only order its contract guarantees.
    /// </summary>
    public static void DequeuesInPriorityOrder<TElement, TPriority>(
        IEnumerable<TElement> expected,
        PriorityQueue<TElement, TPriority>? actual)
    {
        Assert.NotNull(actual);
        foreach (var item in expected)
            Assert.Equal(item, actual.Dequeue());

        Assert.Equal(0, actual.Count);
    }
}
