using System.Reflection;
using System.Runtime.ExceptionServices;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;
using ViShap.Viper.Serialization.Tests.RoundTrip;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins ORC-01, ORC-02 and TYP-01: every case of the existing corpora — the round-trip corpus under
/// each of its profiles and under reference framing, the V0 corpus, the reference graphs of
/// <c>References/</c> and the shapes of <c>Contracts/</c> — writes exactly the bytes the oracle
/// recorded, and a difference is reported with both outputs in hex. Every case is written by the
/// engine's typed codecs, so the oracle holds each codec to the recorded bytes.
/// </summary>
/// <remarks>
/// The oracle invents no case. It runs the tests of those classes as they are written and hashes what
/// they wrote through <see cref="OracleRecorder"/>; each case is keyed by class, test method, data row
/// and the position of the write within the test.
/// </remarks>
public class OracleTests
{
    [Fact]
    public void Oracle_EveryCorpusCase_WritesItsRecordedOutput()
    {
        string report = Oracle.Compare(Oracle.Load(), Collect());

        Assert.True(report.Length == 0, report);
    }

    [Fact]
    public void Oracle_ChangedByte_IsReportedWithBothOutputsInHex()
    {
        var expected = Oracle.Load();
        var (key, output) = Collect().First(write => expected[write.Key].Outputs.Count == 1);

        byte[] changed = output.ToArray();
        changed[^1] ^= 0x01;

        string report = Oracle.Compare(
            expected.Where(entry => entry.Key == key).ToDictionary(),
            [(key, changed)]);

        Assert.Contains($"{key}: first difference at offset {changed.Length - 1}", report);
        Assert.Contains($"expected {Convert.ToHexStringLower(output)}", report);
        Assert.Contains($"actual   {Convert.ToHexStringLower(changed)}", report);
    }

    /// <summary>Runs every test in the oracle's scope and returns what each one wrote, keyed.</summary>
    internal static List<(string Key, byte[] Output)> Collect()
    {
        var writes = new List<(string Key, byte[] Output)>();

        foreach (var (type, key) in Scope())
        {
            foreach (var method in TestMethods(type))
            {
                foreach (var (row, arguments) in Rows(method))
                {
                    string name = row is null ? $"{type.Name}.{method.Name}" : $"{type.Name}.{method.Name}[{row}]";
                    var outputs = OracleRecorder.Collect(() => Invoke(type, method, arguments));

                    for (int index = 0; index < outputs.Count; index++)
                        writes.Add(($"{name}#{index}", Oracle.Normalize(outputs[index], key)));
                }
            }
        }

        return writes;
    }

    private static IEnumerable<(Type Type, byte[]? Key)> Scope()
    {
        yield return (typeof(DefaultCorpusTests), null);
        yield return (typeof(HeaderlessCorpusTests), null);
        yield return (typeof(ProtectedCorpusTests), ProtectedCorpusTests.Key);
        yield return (typeof(DeflateProtectedCorpusTests), DeflateProtectedCorpusTests.Key);
        yield return (typeof(ReferencePreservingCorpus), null);
        yield return (typeof(V0CorpusTests), null);

        string[] namespaces =
        [
            typeof(References.ReferenceIdentityTests).Namespace!,
            typeof(Contracts.KeyedContractTests).Namespace!,
        ];

        var types = typeof(OracleTests).Assembly.GetTypes()
            .Where(type => type is { IsPublic: true, IsAbstract: false, IsClass: true })
            .Where(type => namespaces.Contains(type.Namespace))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var type in types)
            yield return (type, null);
    }

    private static IEnumerable<MethodInfo> TestMethods(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<FactAttribute>() is { Skip: null })
            .OrderBy(method => method.Name, StringComparer.Ordinal);

    private static IEnumerable<(int? Row, object?[]? Arguments)> Rows(MethodInfo method)
    {
        if (method.GetCustomAttribute<TheoryAttribute>() is null)
        {
            yield return (null, null);
            yield break;
        }

        if (method.GetCustomAttributes().Any(attribute => attribute is MemberDataAttribute or ClassDataAttribute))
            throw new InvalidOperationException($"{method.DeclaringType!.Name}.{method.Name} draws its rows from data the oracle cannot enumerate.");

        int row = 0;

        foreach (var data in method.GetCustomAttributes<InlineDataAttribute>())
            yield return (row++, data.GetData(method).Single());
    }

    private static void Invoke(Type type, MethodInfo method, object?[]? arguments)
    {
        object instance = Activator.CreateInstance(type, nonPublic: true)!;

        try
        {
            method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
        }
        finally
        {
            (instance as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The round-trip corpus with reference framing on. No other profile writes the corpus with the
    /// option, so without this one the oracle would cover references only through the graphs of
    /// <c>References/</c>. It is private, so it runs only under the oracle.
    /// </summary>
    private sealed class ReferencePreservingCorpus : Corpus
    {
        protected override BinarySerializer Serializer { get; } = new(
            BinarySerializerOptions.Configure().PreserveReferences().Build());

        protected override BinarySerializer WithLimits(SerializationLimits limits) =>
            new(BinarySerializerOptions.Configure().PreserveReferences().WithLimits(limits).Build());
    }
}
