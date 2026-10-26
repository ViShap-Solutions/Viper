using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>What compiling one documentation example produced.</summary>
/// <param name="IsProgram">Whether the block has top-level statements and so compiled to a program.</param>
/// <param name="Diagnostics">Every warning and error of the compiler and the source generator, located on the page.</param>
/// <param name="Image">The compiled assembly, or <see langword="null"/> when an error stopped it.</param>
internal sealed record CompiledExample(bool IsProgram, IReadOnlyList<string> Diagnostics, byte[]? Image);

/// <summary>
/// Compiles a documentation example the way a consumer's project would: a <c>net10.0</c> console
/// application, or a class library when the block only declares types, with the SDK's implicit global
/// usings, nullable reference types on, the framework and the Viper assemblies referenced, and the
/// Viper source generator running as the package's analyzer runs it. A program is then run in this
/// process.
/// </summary>
internal static class ExampleCompiler
{
    /// <summary>How long a program may run before it is reported as hanging.</summary>
    public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The global usings <c>ImplicitUsings</c> gives a project of <c>Microsoft.NET.Sdk</c>.</summary>
    private const string ImplicitUsings =
        """
        global using global::System;
        global using global::System.Collections.Generic;
        global using global::System.IO;
        global using global::System.Linq;
        global using global::System.Net.Http;
        global using global::System.Threading;
        global using global::System.Threading.Tasks;
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(LoadReferences);

    private static readonly Lazy<ISourceGenerator> Generator = new(LoadGenerator);

    /// <summary>Whether <paramref name="example"/> has top-level statements, and so is a program.</summary>
    public static bool IsProgram(DocumentationExample example) =>
        IsProgram(CSharpSyntaxTree.ParseText(example.Code, ParseOptions, path: example.Page));

    /// <summary>Compiles <paramref name="example"/>, its diagnostics located on its page.</summary>
    public static CompiledExample Compile(DocumentationExample example)
    {
        SyntaxTree code = CSharpSyntaxTree.ParseText(example.Code, ParseOptions, path: example.Page);
        SyntaxTree usings = CSharpSyntaxTree.ParseText(ImplicitUsings, ParseOptions, path: "ImplicitUsings.g.cs");
        bool isProgram = IsProgram(code);

        var options = new CSharpCompilationOptions(
            isProgram ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            "Example_" + Guid.NewGuid().ToString("N"), [code, usings], References.Value, options);

        CSharpGeneratorDriver
            .Create([Generator.Value], parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation generated, out var generatorDiagnostics);

        using var image = new MemoryStream();
        var emitted = generated.Emit(image);

        string[] diagnostics =
        [
            .. generatorDiagnostics.Concat(emitted.Diagnostics)
                .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
                .Select(diagnostic => Locate(diagnostic, example))
                .Distinct()
        ];

        return new CompiledExample(isProgram, diagnostics, emitted.Success ? image.ToArray() : null);
    }

    /// <summary>
    /// Runs a compiled program's entry point with no arguments and returns its exit code; an exception
    /// the program throws is raised as it was thrown, and a program that does not finish within
    /// <see cref="RunTimeout"/> is raised as a <see cref="TimeoutException"/>.
    /// </summary>
    public static int Run(CompiledExample compiled)
    {
        if (!compiled.IsProgram || compiled.Image is null)
            throw new InvalidOperationException("Only a program that compiled can be run.");

        MethodInfo entry = Assembly.Load(compiled.Image).EntryPoint
            ?? throw new InvalidOperationException("The program has no entry point.");
        object?[] arguments = entry.GetParameters().Length == 0 ? [] : [Array.Empty<string>()];

        var run = Task.Factory.StartNew(
            () => entry.Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        if (Task.WhenAny(run, Task.Delay(RunTimeout)).GetAwaiter().GetResult() != run)
            throw new TimeoutException($"The program did not finish within {RunTimeout.TotalSeconds} seconds.");

        return run.GetAwaiter().GetResult() is int code ? code : 0;
    }

    private static bool IsProgram(SyntaxTree code) =>
        ((CompilationUnitSyntax)code.GetRoot()).Members.OfType<GlobalStatementSyntax>().Any();

    private static string Locate(Diagnostic diagnostic, DocumentationExample example)
    {
        var span = diagnostic.Location.GetMappedLineSpan();
        string where = span.IsValid && span.Path == example.Page
            ? $"{example.Page}:{example.Line + span.StartLinePosition.Line}"
            : span.IsValid ? span.Path : example.Page;

        return $"{where}: {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.GetMessage()}";
    }

    /// <summary>
    /// The framework's assemblies and the three a consumer of the meta-package compiles against:
    /// Core, Serialization and Serialization's own dependency.
    /// </summary>
    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The host lists no trusted platform assemblies.");

        IEnumerable<string> framework = trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path)!),
                Path.TrimEndingDirectorySeparator(runtime),
                StringComparison.OrdinalIgnoreCase));

        string[] viper =
        [
            typeof(BinarySerializerException).Assembly.Location,
            typeof(BinarySerializer).Assembly.Location,
            typeof(System.IO.Hashing.XxHash3).Assembly.Location
        ];

        return [.. framework.Concat(viper).Select(path => MetadataReference.CreateFromFile(path))];
    }

    /// <summary>
    /// The Viper source generator, from the copy the build places beside the test assembly. The test
    /// project consumes it as an analyzer, so its assembly is not otherwise loaded here.
    /// </summary>
    private static ISourceGenerator LoadGenerator()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Generator", "ViShap.Viper.Generator.dll");

        if (!File.Exists(path))
            throw new InvalidOperationException($"The source generator was not copied to '{path}'.");

        var generators = Assembly.LoadFrom(path).GetExportedTypes()
            .Where(type => typeof(IIncrementalGenerator).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();

        if (generators.Length != 1)
            throw new InvalidOperationException($"Expected one incremental generator in '{path}', found {generators.Length}.");

        return ((IIncrementalGenerator)Activator.CreateInstance(generators[0])!).AsSourceGenerator();
    }
}
