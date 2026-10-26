using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Adapters.Market;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Suites;

namespace ViShap.Viper.Serialization.Benchmarks.Capabilities;

/// <summary>The capabilities of FAIR-21, each proven by a probe rather than read from a README.</summary>
internal enum Capability
{
    SelfDescription,
    UnknownFields,
    FieldRemoval,
    FieldReordering,
    ReferenceIdentity,
    Cycles,
    Polymorphism,
    DepthLimiting,
    Streams,
    Population,
}

/// <summary>One probe of one adapter configuration.</summary>
internal sealed record CapabilityResult(MarketAdapter Adapter, Capability Capability, ProbeOutcome Outcome);

/// <summary>
/// The capability probes of Benchmark-Plan §7.5. Each is a real round trip over the probe models of the
/// adapter's family, whose outcome is <c>Supported</c>, <c>Partial</c> with the difference named, or
/// <c>Unsupported</c> with what the library does instead (FAIR-22). A probe that can bring a process down
/// — a cycle, a very deep value — runs in a process of its own, so a library without a guard is
/// recorded rather than fatal to the run.
/// </summary>
internal static class Probes
{
    /// <summary>The depth of the depth-limiting probe: far past any stack a recursive codec could keep.</summary>
    internal const int ExcessiveDepth = 100_000;

    private const string ChildMarker = "PROBE|";

    internal static IReadOnlyList<CapabilityResult> RunAll(MarketAdapter adapter) =>
        [.. Enum.GetValues<Capability>().Select(capability => new CapabilityResult(adapter, capability, Run(adapter, capability)))];

    /// <summary>Runs one probe, in a child process when it is one that may crash it.</summary>
    internal static ProbeOutcome Run(MarketAdapter adapter, Capability capability) => capability switch
    {
        Capability.Cycles or Capability.DepthLimiting => InChild(adapter, capability),
        _ => Guarded(() => InProcess(adapter, capability)),
    };

    /// <summary>
    /// The self-description outcome of a format whose values describe themselves — a schema-less reader
    /// walked <paramref name="read"/> payload bytes and found <paramref name="carried"/> — but whose
    /// production does not: no algorithm, length or version for a reader to act on (§6, T1).
    /// </summary>
    internal static ProbeOutcome ValuesOnly(string carried, int read) => ProbeOutcome.Partial(
        $"a reader with no schema walked all {read} bytes: {carried}; nothing of the payload's production — algorithm, length, version — is described");

    /// <summary>The entry point of <c>--probe-child</c>: runs one probe and prints its outcome on one line.</summary>
    internal static int Child(string adapterId, string capability)
    {
        var adapter = MarketRoster.Find(adapterId);
        var outcome = Guarded(() => InProcess(adapter, Enum.Parse<Capability>(capability)));

        Console.WriteLine($"{ChildMarker}{outcome.State}|{Single(outcome.Detail)}");
        return 0;
    }

    private static ProbeOutcome InProcess(MarketAdapter adapter, Capability capability)
    {
        var family = adapter.Family;

        switch (capability)
        {
            case Capability.SelfDescription:
                return adapter.SelfDescription();

            case Capability.UnknownFields:
            {
                var restored = RoundTrip(adapter, Make(family, "ProbeRecordAdded", ("A", 7), ("B", "seven"), ("C", 2.5), ("D", 99L)), "ProbeRecord");
                return Expect(restored, "an older reader skipped a member it does not know", ("A", 7), ("B", "seven"), ("C", 2.5));
            }

            case Capability.FieldRemoval:
            {
                var restored = RoundTrip(adapter, Make(family, "ProbeRecordRemoved", ("A", 7), ("C", 2.5)), "ProbeRecord");
                var outcome = Expect(restored, "a reader kept its default for a member the writer no longer has", ("A", 7), ("C", 2.5));
                return outcome.State == CapabilityState.Supported && Get(restored, "B") is not (null or "")
                    ? ProbeOutcome.Unsupported($"the removed member was read as \"{Get(restored, "B")}\"")
                    : outcome;
            }

            case Capability.FieldReordering:
            {
                var restored = RoundTrip(adapter, Make(family, "ProbeRecord", ("A", 7), ("B", "seven"), ("C", 2.5)), "ProbeRecordReordered");
                return Expect(restored, "a reader declaring the members in another order read each by its key", ("A", 7), ("B", "seven"), ("C", 2.5));
            }

            case Capability.ReferenceIdentity:
            {
                var leaf = Make(family, "ProbeLeaf", ("Name", "shared"));
                var restored = RoundTrip(adapter, Make(family, "ProbePair", ("Left", leaf), ("Right", leaf)), "ProbePair");
                var left = Get(restored, "Left");

                return left is not null && ReferenceEquals(left, Get(restored, "Right"))
                    ? ProbeOutcome.Supported("one instance reached twice is restored as one instance")
                    : ProbeOutcome.Unsupported("one instance reached twice is restored as two equal copies");
            }

            case Capability.Cycles:
            {
                var node = Make(family, "ProbeCycle", ("Name", "loop"));
                Set(node, "Next", node);

                var restored = RoundTrip(adapter, node, "ProbeCycle");
                return restored is not null && ReferenceEquals(Get(restored, "Next"), restored)
                    ? ProbeOutcome.Supported("a value referring to itself is restored referring to itself")
                    : ProbeOutcome.Unsupported("a value referring to itself is not restored as a cycle");
            }

            case Capability.Polymorphism:
            {
                var shapes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ModelFamilies.Resolve(family, "ProbeShape")))!;
                shapes.Add(Make(family, "ProbeCircle", ("Name", "c"), ("Radius", 1.5)));
                shapes.Add(Make(family, "ProbeSquare", ("Name", "s"), ("Side", 2.0)));

                var restored = (IList?)Invoke(adapter, nameof(IBufferedSerializer.Deserialize), shapes.GetType(), Write(adapter, shapes));
                return restored is [var first, var second]
                    && first?.GetType().Name == "ProbeCircle" && second?.GetType().Name == "ProbeSquare"
                    && Equals(Get(first, "Radius"), 1.5) && Equals(Get(second, "Side"), 2.0)
                        ? ProbeOutcome.Supported("each element is restored as its runtime type through a declared base")
                        : ProbeOutcome.Unsupported("an element is not restored as its runtime type");
            }

            case Capability.DepthLimiting:
                return DepthLimit(adapter);

            case Capability.Streams:
                return TwoValuesThroughForwardOnlyStreams(adapter);

            case Capability.Population:
            {
                var payload = Write(adapter, Make(family, "ProbeRecord", ("A", 7), ("B", "seven"), ("C", 2.5)));
                var target = Make(family, "ProbeRecord", ("A", 1), ("B", "one"), ("C", 1.0));

                var filled = typeof(MarketAdapter).GetMethod(nameof(MarketAdapter.Populate), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(target.GetType()).Invoke(adapter, [payload, target]);

                if (filled is null)
                    return ProbeOutcome.Unsupported("no entry point reads into an existing instance; every read creates the root");

                var outcome = Expect(filled, "the payload was read into the instance the caller passed", ("A", 7), ("B", "seven"), ("C", 2.5));
                return outcome.State == CapabilityState.Supported && !ReferenceEquals(filled, target)
                    ? ProbeOutcome.Partial("the read returns a new instance rather than filling the one passed")
                    : outcome;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(capability));
        }
    }

    private static ProbeOutcome DepthLimit(MarketAdapter adapter)
    {
        var family = adapter.Family;
        object? chain = null;

        for (var level = ExcessiveDepth; level > 0; level--)
            chain = Make(family, "ProbeDeep", ("Level", level), ("Next", chain));

        byte[] payload;
        try
        {
            payload = Write(adapter, chain!);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ProbeOutcome.Supported($"a value {ExcessiveDepth:N0} levels deep is refused on write: {exception.GetType().Name}");
        }

        try
        {
            RoundTripRead(adapter, payload, "ProbeDeep");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ProbeOutcome.Supported($"a payload {ExcessiveDepth:N0} levels deep is refused on read: {exception.GetType().Name}");
        }

        return ProbeOutcome.Unsupported($"a value {ExcessiveDepth:N0} levels deep was written and read: no depth limit applies");
    }

    /// <summary>
    /// One value written to a stream that cannot seek and read from one that cannot either; then two
    /// values one after the other, read back one at a time. The first is what a stream entry point is;
    /// the second is what a stream of messages needs.
    /// </summary>
    private static ProbeOutcome TwoValuesThroughForwardOnlyStreams(MarketAdapter adapter)
    {
        var family = adapter.Family;
        var first = Make(family, "ProbeRecord", ("A", 1), ("B", "first"), ("C", 1.5));
        var second = Make(family, "ProbeRecord", ("A", 2), ("B", "second"), ("C", 2.5));
        var type = first.GetType();

        try
        {
            var single = new MemoryStream();
            Invoke(adapter, nameof(IStreamingSerializer.Serialize), type, new ForwardOnlyStream(single), first, streaming: true);

            var restored = Invoke(adapter, nameof(IStreamingSerializer.Deserialize), type, new ForwardOnlyReadStream(single.ToArray()), streaming: true);
            if (!Equals(Get(restored, "A"), 1))
                return ProbeOutcome.Unsupported("one value through streams that cannot seek was not restored");
        }
        catch (Exception exception)
        {
            return ProbeOutcome.Unsupported($"one value through streams that cannot seek failed: {Verification.RoundTripVerifier.Describe(exception)}");
        }

        var buffer = new MemoryStream();
        var destination = new ForwardOnlyStream(buffer);
        Invoke(adapter, nameof(IStreamingSerializer.Serialize), type, destination, first, streaming: true);
        Invoke(adapter, nameof(IStreamingSerializer.Serialize), type, destination, second, streaming: true);

        var source = new ForwardOnlyReadStream(buffer.ToArray());
        object? one, two;

        try
        {
            one = Invoke(adapter, nameof(IStreamingSerializer.Deserialize), type, source, streaming: true);
            two = Invoke(adapter, nameof(IStreamingSerializer.Deserialize), type, source, streaming: true);
        }
        catch (Exception exception)
        {
            return ProbeOutcome.Partial($"one value per stream: a read of two values one after the other failed ({exception.GetType().Name})");
        }

        return Equals(Get(one, "A"), 1) && Equals(Get(two, "A"), 2)
            ? ProbeOutcome.Supported("one value, and two one after the other, through streams that cannot seek, read back one at a time")
            : ProbeOutcome.Partial($"one value per stream: two values one after the other were read as A={Describe(Get(one, "A"))} and A={Describe(Get(two, "A"))}");
    }

    private static ProbeOutcome InChild(MarketAdapter adapter, Capability capability)
    {
        var executable = System.Environment.ProcessPath ?? "dotnet";
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);

        startInfo.ArgumentList.Add("--probe-child");
        startInfo.ArgumentList.Add(adapter.Id);
        startInfo.ArgumentList.Add(capability.ToString());

        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = errors.GetAwaiter().GetResult();

        var line = output.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.StartsWith(ChildMarker, StringComparison.Ordinal));

        if (line is null)
        {
            var reason = process.ExitCode == unchecked((int)0xC00000FD) || output.Contains("Stack overflow", StringComparison.Ordinal)
                ? "stack overflow"
                : $"exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}";

            return ProbeOutcome.Unsupported($"the process terminated ({reason}): nothing stopped the recursion");
        }

        var parts = line[ChildMarker.Length..].Split('|', 2);
        return new ProbeOutcome(Enum.Parse<CapabilityState>(parts[0]), parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static ProbeOutcome Guarded(Func<ProbeOutcome> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception exception)
        {
            var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
            return ProbeOutcome.Unsupported(Verification.RoundTripVerifier.Describe(inner));
        }
    }

    private static ProbeOutcome Expect(object? restored, string supported, params (string Member, object Value)[] expected)
    {
        if (restored is null)
            return ProbeOutcome.Unsupported("nothing was restored");

        foreach (var (member, value) in expected)
        {
            var actual = Get(restored, member);
            if (!Equals(actual, value))
                return ProbeOutcome.Unsupported($"{member} was read as {Describe(actual)} instead of {Describe(value)}");
        }

        return ProbeOutcome.Supported(supported);
    }

    private static object? RoundTrip(MarketAdapter adapter, object value, string readAs) =>
        RoundTripRead(adapter, Write(adapter, value), readAs);

    private static object? RoundTripRead(MarketAdapter adapter, byte[] payload, string readAs) =>
        Invoke(adapter, nameof(IBufferedSerializer.Deserialize), ModelFamilies.Resolve(adapter.Family, readAs), payload);

    private static byte[] Write(MarketAdapter adapter, object value) =>
        (byte[])Invoke(adapter, nameof(IBufferedSerializer.Serialize), value.GetType(), value)!;

    /// <summary>Calls one of the adapter's generic entry points closed over a probe model.</summary>
    private static object? Invoke(MarketAdapter adapter, string name, Type type, object argument, bool streaming = false) =>
        Invoke(adapter, name, type, [argument], streaming);

    private static object? Invoke(MarketAdapter adapter, string name, Type type, object first, object second, bool streaming) =>
        Invoke(adapter, name, type, [first, second], streaming);

    private static object? Invoke(MarketAdapter adapter, string name, Type type, object[] arguments, bool streaming)
    {
        var method = (streaming ? typeof(IStreamingSerializer) : typeof(IBufferedSerializer)).GetMethod(name)!;

        try
        {
            return method.MakeGenericMethod(type).Invoke(adapter, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    private static object Make(ModelFamily family, string type, params (string Member, object? Value)[] members)
    {
        var value = Activator.CreateInstance(ModelFamilies.Resolve(family, type))!;
        foreach (var (member, content) in members)
            Set(value, member, content);

        return value;
    }

    private static void Set(object owner, string member, object? value) =>
        (owner.GetType().GetProperty(member) ?? throw new InvalidOperationException($"{owner.GetType()} has no '{member}'."))
            .SetValue(owner, value);

    private static object? Get(object? owner, string member) => owner?.GetType().GetProperty(member)?.GetValue(owner);

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.GetType().Name,
    };

    internal static string Single(string text) => text.ReplaceLineEndings(" ").Replace('|', '/').Trim();

    /// <summary>Writes the probe matrix as <c>capabilities.csv</c> (FAIR-23).</summary>
    internal static void Write(IReadOnlyList<CapabilityResult> results, string path)
    {
        var csv = new StringBuilder("library,version,configuration,capability,state,detail\n");

        foreach (var result in results)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{Quote(result.Adapter.Library)},{Quote(result.Adapter.Version)},{Quote(result.Adapter.Configuration)},");
            csv.Append(CultureInfo.InvariantCulture, $"{result.Capability},{result.Outcome.State},{Quote(result.Outcome.Detail)}\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
