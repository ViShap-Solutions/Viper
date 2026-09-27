namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Collects what a writer produced while a test runs, so the oracle can hash the output of the
/// existing corpora without restating a single one of their cases. Outside a collection it records
/// nothing, and a test that serializes through it behaves exactly as one that calls
/// <see cref="BinarySerializer.Serialize{T}(T)"/> directly.
/// </summary>
internal static class OracleRecorder
{
    private static readonly AsyncLocal<List<byte[]>?> Current = new();

    /// <summary>
    /// Serializes <paramref name="value"/> exactly as <see cref="BinarySerializer.Serialize{T}(T)"/> does
    /// and, while a collection is running, records a copy of the output.
    /// </summary>
    public static byte[] SerializeRecorded<T>(this BinarySerializer serializer, T value)
    {
        byte[] output = serializer.Serialize(value);

        // A copy, because a test is free to mutate the payload it was handed.
        Current.Value?.Add(output.ToArray());

        return output;
    }

    /// <summary>Runs <paramref name="action"/> and returns every output recorded during it, in order.</summary>
    public static List<byte[]> Collect(Action action)
    {
        var outputs = new List<byte[]>();
        var previous = Current.Value;
        Current.Value = outputs;

        try
        {
            action();
        }
        finally
        {
            Current.Value = previous;
        }

        return outputs;
    }
}
