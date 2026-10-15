namespace ViShap.Viper.Serialization.Benchmarks.Adapters.Market;

/// <summary>
/// Takes the result of an asynchronous library call that a synchronous family measures. A call that
/// completed synchronously — as a write to or a read from a <see cref="MemoryStream"/> does — costs
/// nothing more; one that did not is waited for.
/// </summary>
internal static class Completed
{
    internal static void Wait(ValueTask task)
    {
        if (task.IsCompletedSuccessfully)
            return;

        task.AsTask().GetAwaiter().GetResult();
    }

    internal static T Wait<T>(ValueTask<T> task) =>
        task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();
}
