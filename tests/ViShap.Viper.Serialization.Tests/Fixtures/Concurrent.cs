using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Runs a body on several dedicated threads released together, so a concurrency test races what it
/// means to race. A serial loop would exercise the same code without ever exposing the first touch
/// of a cache to a second thread, which is the only moment these tests are about.
/// </summary>
internal static class Concurrent
{
    /// <summary>How many threads a race uses unless a test asks for another number.</summary>
    public const int Workers = 16;

    /// <summary>
    /// Starts <paramref name="workers"/> threads, releases them from a common barrier, and returns
    /// each result by worker index. The first failure is rethrown with its original stack, so a test
    /// asserting an exception type sees what a worker actually threw.
    /// </summary>
    public static T[] Race<T>(Func<int, T> body, int workers = Workers)
    {
        var results = new T[workers];
        var failures = new ConcurrentQueue<Exception>();
        using var gate = new Barrier(workers);
        var threads = new Thread[workers];

        for (int worker = 0; worker < workers; worker++)
        {
            int index = worker;
            threads[worker] = new Thread(() =>
            {
                gate.SignalAndWait();
                try
                {
                    results[index] = body(index);
                }
                catch (Exception exception)
                {
                    failures.Enqueue(exception);
                }
            })
            {
                IsBackground = true,
                Name = $"race-{index}"
            };

            threads[worker].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        if (failures.TryDequeue(out var failure))
            ExceptionDispatchInfo.Capture(failure).Throw();

        return results;
    }

    /// <summary>Races a body that produces nothing.</summary>
    public static void Race(Action<int> body, int workers = Workers) =>
        Race(worker =>
        {
            body(worker);
            return true;
        }, workers);
}
