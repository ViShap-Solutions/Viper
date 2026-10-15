namespace ViShap.Viper.Serialization.Benchmarks.Capabilities;

/// <summary>What a capability probe found, from a real round trip (FAIR-22).</summary>
internal enum CapabilityState
{
    Supported,
    Partial,
    Unsupported,
}

/// <summary>One probe's outcome: its state and, unless supported, the difference named.</summary>
internal sealed record ProbeOutcome(CapabilityState State, string Detail)
{
    internal static ProbeOutcome Supported(string detail = "") => new(CapabilityState.Supported, detail);

    internal static ProbeOutcome Partial(string detail) => new(CapabilityState.Partial, detail);

    internal static ProbeOutcome Unsupported(string detail) => new(CapabilityState.Unsupported, detail);
}
