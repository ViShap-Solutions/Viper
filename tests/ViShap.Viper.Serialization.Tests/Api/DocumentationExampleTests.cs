using System.Text.RegularExpressions;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins DOC-01…DOC-05: every <c>csharp</c> block of the consumer documentation — <c>docs/</c>, the
/// repository's <c>README.md</c> and the package READMEs — is a complete example that compiles against
/// the current code, with the source generator running, and a block with top-level statements also
/// runs; and every public type of the packages is named on a page of <c>docs/</c>. An example that no
/// longer matches the API fails here, at the page and line it sits on.
/// </summary>
public class DocumentationExampleTests
{
    public static TheoryData<string> Programs { get; } =
        [.. DocumentationExamples.All.Where(ExampleCompiler.IsProgram).Select(example => example.Name)];

    public static TheoryData<string> Libraries { get; } =
        [.. DocumentationExamples.All.Where(example => !ExampleCompiler.IsProgram(example)).Select(example => example.Name)];

    // --- DOC-01: a block with top-level statements compiles and runs ---------------------------

    [Theory]
    [MemberData(nameof(Programs))]
    public void Program_CompilesWithoutDiagnostics_AndRunsToExitCodeZero(string name)
    {
        var example = DocumentationExamples.Named(name);

        var compiled = ExampleCompiler.Compile(example);

        Assert.True(compiled.IsProgram);
        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(0, ExampleCompiler.Run(compiled));
    }

    // --- DOC-02: a block that only declares types compiles as a library ------------------------

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_CompilesWithoutDiagnostics(string name)
    {
        var example = DocumentationExamples.Named(name);

        var compiled = ExampleCompiler.Compile(example);

        Assert.False(compiled.IsProgram);
        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Image);
    }

    // --- DOC-03: the pages in scope ------------------------------------------------------------

    [Fact]
    public void Pages_IncludeTheReadmeOfEveryPackage()
    {
        var declared = Directory
            .EnumerateFiles(Path.Combine(SourceTree.RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(project => (Project: project, Readme: Regex.Match(File.ReadAllText(project), "<PackageReadmeFile>(?<file>[^<]+)</PackageReadmeFile>")))
            .Where(entry => entry.Readme.Success)
            .Select(entry => Path.GetRelativePath(
                    SourceTree.RepositoryRoot,
                    Path.Combine(Path.GetDirectoryName(entry.Project)!, entry.Readme.Groups["file"].Value))
                .Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.Equal(4, declared.Length);
        Assert.All(declared, readme => Assert.Contains(readme, DocumentationExamples.Pages));
        Assert.Contains("README.md", DocumentationExamples.Pages);
        Assert.Contains("docs/getting-started.md", DocumentationExamples.Pages);
    }

    // --- DOC-04: no C# block escapes the rule by its fence -------------------------------------

    [Fact]
    public void Pages_EveryFenceNamesItsLanguage_AndCSharpOnlyAsCsharp()
    {
        string[] aliases = ["cs", "c#", "csharp"];

        var offenders = DocumentationExamples.Pages
            .SelectMany(page => DocumentationExamples
                .FenceLanguages(File.ReadAllText(Path.Combine(SourceTree.RepositoryRoot, page)))
                .Where(language => language.Length == 0 ||
                    (aliases.Contains(language, StringComparer.OrdinalIgnoreCase) && language != DocumentationExamples.Language))
                .Select(language => $"{page}: '{language}'"))
            .ToArray();

        Assert.Empty(offenders);
    }

    // --- DOC-05: every public type is explained ------------------------------------------------

    [Fact]
    public void Docs_NameEveryPublicTypeOfThePackages()
    {
        var docs = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(SourceTree.RepositoryRoot, "docs"), "*.md", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        var unnamed = new[] { typeof(BinarySerializerException).Assembly, typeof(BinarySerializer).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(type => type.Name.Split('`')[0])
            .Select(name => name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !Regex.IsMatch(docs, $@"(?<![\w.]){Regex.Escape(name)}\b"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(unnamed.Length == 0, "Named on no page of docs/: " + string.Join(", ", unnamed));
    }
}
