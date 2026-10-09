using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ViShap.Viper.Generator.Tests.Fixtures;

namespace ViShap.Viper.Generator.Tests;

/// <summary>
/// Pins GEN-05, GEN-06, GEN-08…GEN-10: the generator is inert without a <c>[BinaryContext]</c>,
/// incremental — an unrelated edit re-emits nothing, an edit to one type re-emits its contract alone —
/// deterministic, emits nothing that belongs to the serializer, classifies types by exactly the
/// serializer's tables, and is never a run-time dependency.
/// </summary>
public class PipelineTests
{
    private const string Types = """
        using ViShap.Viper;
        namespace Sample;
        public class First { public int A { get; set; } }
        public class Second { public string? B { get; set; } }
        [BinaryContext(typeof(First), typeof(Second))]
        public partial class Ctx : BinarySerializerContext;
        """;

    private const string Unrelated = "namespace Sample; public class Elsewhere { public int Z; }";

    // --- GEN-05: inert ---------------------------------------------------------------------------

    [Fact]
    public void AProjectWithoutAContext_GetsNothing()
    {
        var run = Harness.Run("using ViShap.Viper; namespace Sample; [BinaryContract] public class T { [BinaryKey(1)] public int A { get; set; } }");

        Assert.Empty(run.Sources);
        Assert.Empty(run.Diagnostics);
    }

    // --- GEN-06: incremental -----------------------------------------------------------------------

    [Fact]
    public void AnUnrelatedEdit_ReEmitsNoContract()
    {
        var compilation = Harness.Compile(Types, Unrelated);
        var driver = Harness.Driver().RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            CSharpSyntaxTree.ParseText("namespace Sample; public class Elsewhere { public int Z; public int Y; }", (CSharpParseOptions)compilation.SyntaxTrees.First().Options, path: "Source1.cs"));
        var result = driver.RunGenerators(edited).GetRunResult().Results.Single();

        var steps = result.TrackedSteps[ContractGenerator.ContractStep].SelectMany(step => step.Outputs).ToArray();
        Assert.NotEmpty(steps);
        Assert.All(steps, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"A contract step ran as {output.Reason}."));
    }

    [Fact]
    public void AnEditToOneType_ReEmitsThatContractAlone()
    {
        var compilation = Harness.Compile(Types);
        var driver = Harness.Driver().RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Types.Replace("public int A { get; set; }", "public long A { get; set; }"), (CSharpParseOptions)compilation.SyntaxTrees.First().Options, path: "Source0.cs"));
        var result = driver.RunGenerators(edited).GetRunResult().Results.Single();

        var reasons = result.TrackedSteps[ContractGenerator.ContractStep]
            .SelectMany(step => step.Outputs)
            .Select(output => (Contract: ((ValueTuple<ContextHeader, ContractModel>)output.Value).Item2.ClassName, output.Reason))
            .ToDictionary(entry => entry.Contract, entry => entry.Reason);

        Assert.Equal(IncrementalStepRunReason.Modified, reasons["Sample_FirstContract"]);
        Assert.Contains(reasons["Sample_SecondContract"], new[] { IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Cached });
    }

    // --- GEN-08: deterministic ----------------------------------------------------------------------

    [Fact]
    public void TheSameInput_InAnyOrder_GivesTheSameOutputWithUnixLineEndings()
    {
        var first = Harness.Run(Types, Unrelated).Sources;
        var second = Harness.Run(Unrelated, Types).Sources;

        Assert.Equal(first.Keys.Order(StringComparer.Ordinal), second.Keys.Order(StringComparer.Ordinal));
        Assert.All(first, source =>
        {
            Assert.Equal(source.Value, second[source.Key]);
            Assert.DoesNotContain('\r', source.Value);
        });
    }

    // --- GEN-09: nothing that belongs to the serializer -----------------------------------------------

    [Fact]
    public void TheEmittedSource_HoldsNoLoopNoLengthNoCountNoTagAndNoReflection()
    {
        var run = Harness.Run(File.ReadAllText(Path.Combine(Repository.Root, "tests", "ViShap.Viper.Generator.Tests", "ByteIdentityTests.cs"))
            .Split("\"\"\"")[1]);
        Assert.NotEmpty(run.Sources);

        string[] forbidden =
        [
            @"\bfor\s*\(", @"\bforeach\b", @"\bwhile\b", @"\bgoto\b", @"\bstackalloc\b",
            @"\bWireReader\b", @"\bWireWriter\b", @"\bElementCount\b", @"\bLimits\b", @"\bSerializationLimits\b",
            @"\.Length\b", @"\.Count\b", @"\bWriteByte\b", @"\bReadByte\b", @"\bSystem\.Reflection\b",
            @"\bExpression\b", @"\bdynamic\b", @"\bActivator\b", @"\bBinaryUnion\b"
        ];

        Assert.All(run.Sources, source => Assert.All(forbidden, pattern =>
            Assert.False(Regex.IsMatch(source.Value, pattern), $"{source.Key} matches '{pattern}'.")));
    }

    // --- GEN-10: the serializer's tables, and no run-time dependency ------------------------------------

    [Fact]
    public void TheClassificationTables_AreTheSerializersOwn()
    {
        var registry = typeof(BinarySerializer).Assembly.GetType("ViShap.Viper.Engine.FormatterRegistry", throwOnError: true)!;

        var scalars = ((System.Collections.IEnumerable)registry.GetField("Scalars", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
            .Cast<object>()
            .Select(entry => ((Type)entry.GetType().GetProperty("Key")!.GetValue(entry)!).FullName!)
            .Order(StringComparer.Ordinal);

        var shapes = ((System.Collections.IEnumerable)registry.GetField("Shapes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
            .Cast<object>()
            .Select(entry => ((Type)entry.GetType().GetProperty("Key")!.GetValue(entry)!).FullName!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(scalars, TypeShapes.Scalars.Order(StringComparer.Ordinal));
        Assert.Equal(shapes, TypeShapes.GenericDefinitions.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheGenerator_ReferencesOnlyRoslynAndTheStandardLibrary_AndNothingReferencesIt()
    {
        var references = typeof(ContractGenerator).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();
        Assert.All(references, name => Assert.True(
            name is "netstandard" || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) || name.StartsWith("System.", StringComparison.Ordinal),
            $"The generator references '{name}'."));

        foreach (var shipped in new[] { typeof(BinarySerializer).Assembly, typeof(BinaryKeyAttribute).Assembly })
            Assert.DoesNotContain(shipped.GetReferencedAssemblies(), name => name.Name == "ViShap.Viper.Generator");
    }
}
