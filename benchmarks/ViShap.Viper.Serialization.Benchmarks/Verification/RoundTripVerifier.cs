using System.Globalization;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Config;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Verification;

internal enum VerificationState
{
    Supported,
    Partial,
    Unsupported,
    Failed,
}

internal sealed record VerificationResult(
    string Adapter,
    string Dataset,
    VerificationState State,
    int BufferedBytes,
    int StreamedBytes,
    string Detail);

/// <summary>
/// Runs every (adapter, dataset) pair through both entry-point families and compares the result with
/// the source. A pair that does not verify produces no timing (Benchmark-Plan FAIR-24, FAIR-25).
/// </summary>
internal static class RoundTripVerifier
{
    internal static IReadOnlyList<VerificationResult> VerifyAll()
    {
        var results = new List<VerificationResult>();

        foreach (var profile in ViperProfiles.All)
        {
            var adapter = new ViperAdapter(profile);
            var references = profile == ViperProfile.PreserveReferences;

            foreach (var dataset in Corpus.All)
            {
                if (dataset.RequiresReferences && !references)
                {
                    results.Add(new VerificationResult(
                        adapter.Name, dataset.Id, VerificationState.Unsupported, 0, 0,
                        "the value contains a cycle, which needs PreserveReferences (§16)"));

                    continue;
                }

                results.Add(Verify(adapter, adapter, dataset, references));
            }
        }

        return results;
    }

    internal static VerificationResult Verify(
        IBufferedSerializer buffered,
        IStreamingSerializer streaming,
        Dataset dataset,
        bool identityExpected = true)
    {
        try
        {
            var payload = dataset.Serialize(buffered);
            var restored = dataset.Deserialize(buffered, payload);

            if (!StructuralComparer.Equal(dataset.BoxedValue, restored, out var difference))
            {
                return new VerificationResult(buffered.Name, dataset.Id, VerificationState.Failed, payload.Length, 0, difference);
            }

            using var destination = new MemoryStream();
            dataset.SerializeTo(streaming, destination);

            destination.Position = 0;
            var streamedRestored = dataset.DeserializeFrom(streaming, destination);

            if (!StructuralComparer.Equal(dataset.BoxedValue, streamedRestored, out var streamDifference))
            {
                return new VerificationResult(
                    buffered.Name, dataset.Id, VerificationState.Failed, payload.Length, (int)destination.Length,
                    $"stream: {streamDifference}");
            }

            if (dataset.IdentitySensitive)
            {
                var identity = restored is null ? "nothing was restored" : dataset.VerifyIdentity(restored);

                if (identity is not null)
                {
                    return new VerificationResult(
                        buffered.Name, dataset.Id,
                        identityExpected ? VerificationState.Failed : VerificationState.Partial,
                        payload.Length, (int)destination.Length,
                        identityExpected ? identity : $"contents restored, identity not: {identity}");
                }
            }

            return new VerificationResult(
                buffered.Name, dataset.Id, VerificationState.Supported, payload.Length, (int)destination.Length,
                string.Empty);
        }
        catch (Exception exception)
        {
            return new VerificationResult(
                buffered.Name, dataset.Id, VerificationState.Failed, 0, 0,
                $"{exception.GetType().Name}: {Single(exception.Message)}");
        }
    }

    internal static void Write(IReadOnlyList<VerificationResult> results, string path)
    {
        var csv = new StringBuilder("adapter,dataset,state,buffered_bytes,streamed_bytes,detail\n");

        foreach (var result in results)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{Quote(result.Adapter)},{Quote(result.Dataset)},{result.State},");
            csv.Append(CultureInfo.InvariantCulture, $"{result.BufferedBytes},{result.StreamedBytes},{Quote(result.Detail)}\n");
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Single(string message) =>
        message.ReplaceLineEndings(" ").Trim();
}
