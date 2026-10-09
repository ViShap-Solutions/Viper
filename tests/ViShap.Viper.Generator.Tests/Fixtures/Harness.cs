using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ViShap.Viper.Generator.Tests.Fixtures;

/// <summary>
/// Compiles C# sources against the built Viper assemblies, runs the source generator over them the way
/// the compiler does, and — when asked — loads the result, so a test can look at what was generated,
/// at what was reported, and at how the generated contracts behave at run time.
/// </summary>
internal static class Harness
{
    private static readonly CSharpParseOptions Parse = new(LanguageVersion.Latest);

    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) ||
                           Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(BinaryKeyAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(BinarySerializer).Assembly.Location)
    ];

    /// <summary>The compilation of <paramref name="sources"/>, each named by its index.</summary>
    public static CSharpCompilation Compile(params string[] sources) =>
        CSharpCompilation.Create(
            "Sample",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, Parse, path: $"Source{index}.cs")),
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));

    /// <summary>A driver over the generator that records the steps of every run, for incremental tests.</summary>
    public static GeneratorDriver Driver() =>
        CSharpGeneratorDriver.Create(
            [new ContractGenerator().AsSourceGenerator()],
            parseOptions: Parse,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    /// <summary>Runs the generator over <paramref name="sources"/>.</summary>
    public static Generated Run(params string[] sources)
    {
        var compilation = Compile(sources);
        var driver = Driver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new Generated(compilation, output, driver.GetRunResult(), diagnostics);
    }

    /// <summary>What one run of the generator produced.</summary>
    public sealed record Generated(
        Compilation Input,
        Compilation Output,
        GeneratorDriverRunResult Result,
        ImmutableArray<Diagnostic> DriverDiagnostics)
    {
        /// <summary>The generated sources by hint name.</summary>
        public IReadOnlyDictionary<string, string> Sources =>
            Result.Results.SelectMany(result => result.GeneratedSources)
                .ToDictionary(source => source.HintName, source => source.SourceText.ToString(), StringComparer.Ordinal);

        /// <summary>The diagnostics the generator reported.</summary>
        public ImmutableArray<Diagnostic> Diagnostics => [.. Result.Results.SelectMany(result => result.Diagnostics)];

        /// <summary>The compiler's errors over the sources and the generated code together.</summary>
        public ImmutableArray<Diagnostic> CompilerErrors =>
            [.. Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];

        /// <summary>The one generated source whose hint name ends with <paramref name="suffix"/>.</summary>
        public string Source(string suffix) =>
            Assert.Single(Sources, source => source.Key.EndsWith(suffix, StringComparison.Ordinal)).Value;

        /// <summary>Compiles the sources together with the generated code and loads the assembly.</summary>
        public Assembly Load()
        {
            Assert.Empty(CompilerErrors);
            return Harness.Load(Output);
        }

        /// <summary>Compiles the sources alone — without what was generated — and loads the assembly.</summary>
        public Assembly LoadWithoutGenerated() => Harness.Load(Input);
    }

    private static Assembly Load(Compilation compilation)
    {
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        image.Position = 0;
        return new AssemblyLoadContext($"sample-{Guid.NewGuid():N}", isCollectible: true).LoadFromStream(image);
    }
}
