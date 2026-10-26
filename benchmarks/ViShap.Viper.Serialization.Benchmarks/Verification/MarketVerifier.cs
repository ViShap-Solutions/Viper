using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Adapters.Market;
using ViShap.Viper.Serialization.Benchmarks.Capabilities;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Schema;

namespace ViShap.Viper.Serialization.Benchmarks.Verification;

/// <summary>One (adapter configuration, dataset) pair of the market, verified.</summary>
internal sealed record MarketPairResult(MarketAdapter Adapter, string Dataset, VerificationState State, int BufferedBytes, int StreamedBytes, string Detail);

/// <summary>Whether a configuration belongs to a tier, by its intent and its probes (TIER-04).</summary>
internal sealed record TierPlacement(MarketAdapter Adapter, Tier Tier, bool Intended, bool Qualified, string Reason);

/// <summary>Everything Track B proves before a timing exists.</summary>
internal sealed record MarketVerification(
    IReadOnlyList<string> SchemaFailures,
    IReadOnlyList<CapabilityResult> Capabilities,
    IReadOnlyList<TierPlacement> Tiers,
    IReadOnlyList<MarketPairResult> Pairs)
{
    internal int Failed => SchemaFailures.Count + Pairs.Count(pair => pair.State == VerificationState.Failed);
}

/// <summary>
/// The fairness machinery of Benchmark-Plan §7.4–§7.6 for Track B, run before any timing: every model
/// family and every projected dataset is checked against the schema (<see cref="ModelSchema"/>,
/// FAIR-19), every configuration probed (FAIR-21…FAIR-23), its tiers placed from the probes (TIER-04),
/// and every (configuration, dataset) pair of its tiers round-tripped through each family it implements.
/// </summary>
internal static class MarketVerifier
{
    /// <summary>The evolution read of WL-15: DATA-13 written as <c>KeyedOrder</c> and read as <c>KeyedOrderV2</c>.</summary>
    internal const string Evolution = "DATA-13/evolution";

    internal static MarketVerification VerifyAll()
    {
        var schema = ModelSchema.Failures();
        var capabilities = MarketRoster.All.SelectMany(Probes.RunAll).ToList();
        var tiers = MarketRoster.All.SelectMany(adapter => Place(adapter, capabilities)).ToList();
        var pairs = MarketRoster.All.SelectMany(adapter => VerifyPairs(adapter, capabilities)).ToList();

        return new MarketVerification(schema, capabilities, tiers, pairs);
    }

    private static IEnumerable<TierPlacement> Place(MarketAdapter adapter, IReadOnlyList<CapabilityResult> capabilities)
    {
        ProbeOutcome Outcome(Capability capability) =>
            capabilities.Single(result => result.Adapter == adapter && result.Capability == capability).Outcome;

        (bool, string) Requires(params Capability[] required)
        {
            var missing = required.Where(capability => Outcome(capability).State != CapabilityState.Supported).ToArray();
            return missing.Length == 0
                ? (true, "every probe the tier requires is Supported")
                : (false, string.Join("; ", missing.Select(capability => $"{capability}: {Outcome(capability).Detail}")));
        }

        var selfDescription = Outcome(Capability.SelfDescription);

        foreach (var tier in new[] { Tier.T0, Tier.T1, Tier.T2, Tier.T3 })
        {
            var (qualified, reason) = tier switch
            {
                Tier.T0 => (true, "the closed-world tier asks for no capability"),
                Tier.T1 => selfDescription.State == CapabilityState.Unsupported
                    ? (false, $"SelfDescription: {selfDescription.Detail}")
                    : (true, selfDescription.State == CapabilityState.Supported
                        ? "SelfDescription is Supported"
                        : $"SelfDescription is Partial: {selfDescription.Detail}"),
                Tier.T2 => Requires(Capability.UnknownFields, Capability.FieldRemoval, Capability.FieldReordering),
                _ => Requires(Capability.ReferenceIdentity, Capability.Cycles, Capability.Polymorphism),
            };

            var intended = (adapter.Intended & tier) != 0;
            if (intended || qualified && tier != Tier.T0)
                yield return new TierPlacement(adapter, tier, intended, qualified, reason);
        }
    }

    private static IEnumerable<MarketPairResult> VerifyPairs(MarketAdapter adapter, IReadOnlyList<CapabilityResult> capabilities)
    {
        ProbeOutcome Outcome(Capability capability) =>
            capabilities.Single(result => result.Adapter == adapter && result.Capability == capability).Outcome;

        var cycles = Outcome(Capability.Cycles);
        var identity = Outcome(Capability.ReferenceIdentity).State == CapabilityState.Supported;

        foreach (var dataset in MarketCorpus.For(adapter.Family))
        {
            if ((MarketCorpus.TiersOf(dataset.Id) & adapter.Intended) == 0)
                continue;

            if (dataset.RequiresReferences && cycles.State != CapabilityState.Supported)
            {
                yield return new MarketPairResult(adapter, dataset.Id, VerificationState.Unsupported, 0, 0, $"the cycles probe: {cycles.Detail}");
                continue;
            }

            var result = RoundTripVerifier.Verify(adapter, adapter, dataset, identityExpected: identity);
            var state = result.State;
            var detail = result.Detail;

            if (state is VerificationState.Supported or VerificationState.Partial && EntryPoints(adapter, dataset) is { } failure)
            {
                state = VerificationState.Failed;
                detail = failure;
            }

            yield return Explain(new MarketPairResult(adapter, dataset.Id, state, result.BufferedBytes, result.StreamedBytes, detail));
        }

        if ((adapter.Intended & Tier.T2) != 0)
            yield return Explain(VerifyEvolution(adapter));
    }

    /// <summary>
    /// A failed pair the adapter declared, by the difference verification observed, becomes
    /// <c>Unsupported</c> with the library's reason; any other failure stays <c>Failed</c>.
    /// </summary>
    private static MarketPairResult Explain(MarketPairResult result) =>
        result.State == VerificationState.Failed
        && result.Adapter.Limitations.FirstOrDefault(limitation => limitation.Covers(result.Dataset, result.Detail)) is { } declared
            ? result with { State = VerificationState.Unsupported, Detail = $"{declared.Reason} (observed: {result.Detail})" }
            : result;

    /// <summary>
    /// The buffer family and the span read, where the adapter implements them: each restores the value
    /// the buffered round trip restored.
    /// </summary>
    private static string? EntryPoints(MarketAdapter adapter, Dataset dataset)
    {
        try
        {
            if (adapter is IBufferWriterSerializer buffer)
            {
                var writer = new ArrayBufferWriter<byte>();
                dataset.SerializeInto(buffer, writer);
                var payload = writer.WrittenSpan.ToArray();

                if (!StructuralComparer.Equal(dataset.BoxedValue, dataset.DeserializeSequence(buffer, Suites.Segments.Of(payload, count: 4)), out var difference))
                    return $"IBufferWriter → ReadOnlySequence: {difference}";

                if (adapter is ISpanDeserializer span
                    && !StructuralComparer.Equal(dataset.BoxedValue, dataset.DeserializeSpan(span, payload), out var spanDifference))
                    return $"ReadOnlySpan: {spanDifference}";
            }

            return null;
        }
        catch (Exception exception)
        {
            return $"entry points: {RoundTripVerifier.Describe(exception)}";
        }
    }

    /// <summary>
    /// WL-15's pair: the orders of DATA-13 read by the model that removed, added and reordered keys. The
    /// members both sides know must survive, and the ones only the reader knows keep their defaults.
    /// </summary>
    private static MarketPairResult VerifyEvolution(MarketAdapter adapter)
    {
        try
        {
            var dataset = MarketCorpus.Find(adapter.Family, "DATA-13");
            var payload = dataset.Serialize(adapter);
            var readAs = ModelFamilies.Map(typeof(List<Models.Viper.KeyedOrderV2>), adapter.Family);

            var restored = (IList?)typeof(IBufferedSerializer).GetMethod(nameof(IBufferedSerializer.Deserialize))!
                .MakeGenericMethod(readAs).Invoke(adapter, [payload]);

            var source = (IList)dataset.BoxedValue;
            if (restored is null || restored.Count != source.Count)
                return new MarketPairResult(adapter, Evolution, VerificationState.Failed, payload.Length, 0, $"{restored?.Count ?? 0} orders read of {source.Count}");

            for (var i = 0; i < source.Count; i++)
            {
                foreach (var member in new[] { "Id", "Total", "Revision", "Lines" })
                {
                    if (!StructuralComparer.Equal(Graph.Member(source[i], member), Graph.Member(restored[i], member), out var difference))
                        return new MarketPairResult(adapter, Evolution, VerificationState.Failed, payload.Length, 0, $"[{i}].{member}: {difference}");
                }

                if (Graph.Member(restored[i], "Warehouse") is not null || Graph.Member(restored[i], "Expedited") is not false)
                    return new MarketPairResult(adapter, Evolution, VerificationState.Failed, payload.Length, 0, $"[{i}]: a member the writer never had is not at its default");
            }

            return new MarketPairResult(adapter, Evolution, VerificationState.Supported, payload.Length, 0, string.Empty);
        }
        catch (Exception exception)
        {
            var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
            return new MarketPairResult(adapter, Evolution, VerificationState.Failed, 0, 0, RoundTripVerifier.Describe(inner));
        }
    }

    // --- artifacts --------------------------------------------------------------------------------

    internal static void Write(MarketVerification verification, string directory)
    {
        Directory.CreateDirectory(directory);

        Probes.Write(verification.Capabilities, Path.Combine(directory, "capabilities.csv"));

        var pairs = new StringBuilder("library,version,configuration,dataset,state,buffered_bytes,streamed_bytes,detail\n");
        foreach (var pair in verification.Pairs)
        {
            pairs.Append(CultureInfo.InvariantCulture,
                $"{Quote(pair.Adapter.Library)},{Quote(pair.Adapter.Version)},{Quote(pair.Adapter.Configuration)},{Quote(pair.Dataset)},");
            pairs.Append(CultureInfo.InvariantCulture, $"{pair.State},{pair.BufferedBytes},{pair.StreamedBytes},{Quote(pair.Detail)}\n");
        }

        File.WriteAllText(Path.Combine(directory, "market-verification.csv"), pairs.ToString(), Encoding.UTF8);

        var tiers = new StringBuilder("tier,library,configuration,intended,qualified,reason\n");
        foreach (var placement in verification.Tiers.OrderBy(placement => placement.Tier))
        {
            tiers.Append(CultureInfo.InvariantCulture,
                $"{placement.Tier},{Quote(placement.Adapter.Library)},{Quote(placement.Adapter.Configuration)},");
            tiers.Append(CultureInfo.InvariantCulture, $"{placement.Intended},{placement.Qualified},{Quote(placement.Reason)}\n");
        }

        File.WriteAllText(Path.Combine(directory, "tiers.csv"), tiers.ToString(), Encoding.UTF8);

        var rationale = new StringBuilder("library,version,configuration,mode,options,not_enabled,faster_mode_not_used\n");
        foreach (var adapter in MarketRoster.All)
        {
            var r = adapter.Rationale;
            rationale.Append(CultureInfo.InvariantCulture,
                $"{Quote(adapter.Library)},{Quote(adapter.Version)},{Quote(adapter.Configuration)},{Quote(r.Mode)},{Quote(r.Options)},{Quote(r.NotEnabled)},{Quote(r.FasterModeNotUsed)}\n");
        }

        File.WriteAllText(Path.Combine(directory, "rationale.csv"), rationale.ToString(), Encoding.UTF8);

        var differences = new StringBuilder("family,type,member,difference,reason\n");
        foreach (var difference in ModelFamilies.Differences)
        {
            differences.Append(CultureInfo.InvariantCulture,
                $"{difference.Family},{Quote(difference.Type)},{Quote(difference.Member)},{Quote(difference.Difference)},{Quote(difference.Reason)}\n");
        }

        File.WriteAllText(Path.Combine(directory, "model-differences.csv"), differences.ToString(), Encoding.UTF8);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
