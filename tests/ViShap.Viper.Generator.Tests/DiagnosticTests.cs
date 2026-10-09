using System.Reflection;
using Microsoft.CodeAnalysis;
using ViShap.Viper.Exceptions;
using ViShap.Viper.Generator.Tests.Fixtures;

namespace ViShap.Viper.Generator.Tests;

/// <summary>
/// Pins GEN-02 and GEN-03: every diagnostic id is reported for the source that earns it, with its
/// severity, the type and the member named in its message and a link into <c>docs/generator.md</c>;
/// and every error the generator reports for a type is a type the serializer refuses when it describes
/// it by reflection — the same sources, compiled without the generated code, throw
/// <see cref="BinaryTypeException"/> on first use.
/// </summary>
public class DiagnosticTests
{
    private const string Usings = "using System;\nusing ViShap.Viper;\nnamespace Sample;\n";

    /// <summary>Id, the source that earns it, a fragment of its message, and — for an error the runtime shares — the declared and the runtime type to write.</summary>
    public static TheoryData<string, string, string, string?, string?> Cases() => new()
    {
        { "VPR001", "[BinaryContext(typeof(int))] public class Ctx : BinarySerializerContext;", "'Sample.Ctx'", null, null },
        { "VPR002", "[BinaryContext(typeof(int))] public partial class Ctx;", "does not derive from ViShap.Viper.BinarySerializerContext", null, null },
        { "VPR003", "[BinaryContext(typeof(int))] public partial class Ctx : BinarySerializerContext { public Ctx() { } }", "a parameterless constructor", null, null },
        { "VPR004", "public class T { [BinaryKey(1)] public int A { get; set; } }", "'Sample.T' member 'A'", "T", "T" },
        { "VPR005", "public class T { [BinaryInclude, BinaryIgnore] private int _a; }", "'Sample.T' member '_a'", "T", "T" },
        { "VPR006", "public class T { [BinaryOrder(1)] public int A { get; set; } [BinaryOrder(1)] public int B { get; set; } }", "A, B", "T", "T" },
        { "VPR007", "[BinaryContract] public class T { [BinaryKey(1), BinaryInclude] public int A { get; set; } }", "'Sample.T' member 'A'", "T", "T" },
        { "VPR008", "[BinaryContract] public class T { [BinaryKey(1), BinaryOrder(1)] public int A { get; set; } }", "'Sample.T' member 'A'", "T", "T" },
        { "VPR009", "[BinaryContract] public class T { [BinaryKey(1), BinaryIgnore] public int A { get; set; } }", "'Sample.T' member 'A'", "T", "T" },
        { "VPR010", "[BinaryContract] public class T { [BinaryKey(1)] public int A { get; set; } public int B { get; set; } }", "member 'B'", "T", "T" },
        { "VPR011", "[BinaryContract] public class T { [BinaryKey(-1)] public int A { get; set; } }", "[BinaryKey(-1)]", "T", "T" },
        { "VPR012", "[BinaryContract] public class T { [BinaryKey(1)] public int A { get; set; } [BinaryKey(1)] public int B { get; set; } }", "A, B", "T", "T" },
        { "VPR013", "public class T { public Action? Callback { get; set; } }", "'Sample.T' member 'Callback'", "T", "T" },
        { "VPR014", "public unsafe class T { public int* Pointer; }", "'Sample.T' member 'Pointer'", "T", "T" },
        { "VPR015", "[BinaryUnion(300, typeof(D))] public abstract class T; public class D : T;", "tag 300", "T", "D" },
        { "VPR016", "[BinaryUnion(1, typeof(D))] [BinaryUnion(1, typeof(E))] public abstract class T; public class D : T; public class E : T;", "tag 1", "T", "D" },
        { "VPR017", "[BinaryUnion(1, typeof(D))] public abstract class T; public class D;", "'Sample.D'", null, null },
        { "VPR018", "public abstract class T { public int A { get; set; } }", "'Sample.T' is abstract", null, null },
        { "VPR019", "public class T : Exception { }", "derives from 'System.Exception'", null, null }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Generator_ReportsTheDiagnosticForItsSource(string id, string source, string fragment, string? declared, string? runtime)
    {
        string context = source.Contains("BinaryContext", StringComparison.Ordinal)
            ? string.Empty
            : "\n[BinaryContext(typeof(T))] public partial class Ctx : BinarySerializerContext;";

        var run = Harness.Run(Usings + source + context);

        var diagnostic = Assert.Single(run.Diagnostics, candidate => candidate.Id == id);
        var descriptor = diagnostic.Descriptor;

        Assert.Contains(fragment, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal($"https://github.com/ViShap-Solutions/Viper/blob/main/docs/generator.md#{id.ToLowerInvariant()}", descriptor.HelpLinkUri);
        Assert.Equal(int.Parse(id[3..], System.Globalization.CultureInfo.InvariantCulture) <= 17 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
        Assert.All(run.Diagnostics, other => Assert.True(other.Id == id || other.Severity == DiagnosticSeverity.Warning, $"Unexpected {other.Id}: {other.GetMessage()}"));

        if (declared is null)
            return;

        // The same type described by reflection is refused the first time it is written.
        var assembly = run.LoadWithoutGenerated();
        var declaredType = assembly.GetType($"Sample.{declared}", throwOnError: true)!;
        object value = Activator.CreateInstance(assembly.GetType($"Sample.{runtime}", throwOnError: true)!, nonPublic: true)!;

        var serialize = typeof(BinarySerializer).GetMethod(nameof(BinarySerializer.Serialize), [Type.MakeGenericMethodParameter(0)])!
            .MakeGenericMethod(declaredType);
        var thrown = Assert.Throws<TargetInvocationException>(() => serialize.Invoke(new BinarySerializer(), [value]));
        Assert.IsType<BinaryTypeException>(thrown.InnerException);
    }

    [Fact]
    public void EveryDescriptor_HasACase()
    {
        var covered = Cases().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        Assert.All(Descriptors.Each, descriptor => Assert.Contains(descriptor.Id, covered));
    }

    [Fact]
    public void AValidType_IsReportedNothingAndAcceptedAtRunTime()
    {
        var run = Harness.Run(Usings + """
            [BinaryContract] public class T { [BinaryKey(1)] public int A { get; set; } [BinaryIgnore] public Action? Callback { get; set; } }
            [BinaryContext(typeof(T))] public partial class Ctx : BinarySerializerContext;
            """);

        Assert.Empty(run.Diagnostics);
        var assembly = run.LoadWithoutGenerated();
        Assert.NotEmpty(new BinarySerializer().SerializeObject(assembly.GetType("Sample.T")!, Activator.CreateInstance(assembly.GetType("Sample.T")!)!));
    }
}

/// <summary>Writes a value whose type is known only at run time.</summary>
internal static class UntypedSerialize
{
    public static byte[] SerializeObject(this BinarySerializer serializer, Type type, object value) =>
        (byte[])typeof(BinarySerializer).GetMethod(nameof(BinarySerializer.Serialize), [Type.MakeGenericMethodParameter(0)])!
            .MakeGenericMethod(type)
            .Invoke(serializer, [value])!;
}
