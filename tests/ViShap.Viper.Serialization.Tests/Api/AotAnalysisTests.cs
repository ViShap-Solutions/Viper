using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using ViShap.Viper.Diagnostics;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins EXT-07: the reflection path states its requirements where a consumer meets it. Every public
/// entry point that encodes or decodes a value of the caller's type carries
/// <see cref="RequiresUnreferencedCodeAttribute"/> and <see cref="RequiresDynamicCodeAttribute"/>, and
/// no other public member carries either. A consumer project built with native AOT analysis —
/// <c>tests/ViShap.Viper.AotConsumer</c>, which calls every one of those entry points and the rest of
/// the surface besides — reports exactly the calls to the annotated entry points, and nothing from
/// the packages themselves, which are compiled afresh under the same analysis.
/// </summary>
public class AotAnalysisTests
{
    private const string Consumer = "ViShap.Viper.AotConsumer";

    private static readonly Regex Diagnostic = new(
        @"^(?<file>.+?)\((?<line>\d+),\d+\): warning (?<code>IL\d{4}): (?<message>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex Marker = new(@"//(?<codes>(?:\s+IL\d{4})+)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex Member = new(@"'(?<member>[^']+)'", RegexOptions.CultureInvariant);

    [Fact]
    public void EntryPointsThatEncodeTheCallersType_CarryBothRequirements_AndNothingElseDoes()
    {
        var annotated = AnnotatedPublicMembers();

        MethodInfo[] expected =
        [
            .. typeof(BinarySerializer).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.IsGenericMethodDefinition),
            .. typeof(BinaryFormatDumper).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.IsGenericMethodDefinition)
        ];

        Assert.Equal(26, expected.Length);
        Assert.Equal(
            expected.Select(Describe).Order(StringComparer.Ordinal),
            annotated.Unreferenced.Select(Describe).Order(StringComparer.Ordinal));
        Assert.Equal(
            expected.Select(Describe).Order(StringComparer.Ordinal),
            annotated.Dynamic.Select(Describe).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ConsumerBuiltWithAotAnalysis_ReportsTheAnnotatedEntryPoints_AndNothingElse()
    {
        string project = Path.Combine(SourceTree.RepositoryRoot, "tests", Consumer);
        string program = Path.Combine(project, "Program.cs");

        var expected = File.ReadAllLines(program)
            .Select((text, index) => (Line: index + 1, Match: Marker.Match(text)))
            .Where(line => line.Match.Success)
            .SelectMany(line => line.Match.Groups["codes"].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(code => $"{line.Line} {code}"))
            .ToHashSet(StringComparer.Ordinal);

        var reported = Build(Path.Combine(project, Consumer + ".csproj"));

        var foreign = reported
            .Where(warning => !string.Equals(Path.GetFullPath(warning.File), Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase))
            .Select(warning => $"{warning.File}({warning.Line}): {warning.Code} {warning.Message}")
            .ToArray();

        Assert.True(foreign.Length == 0, "Trimming or AOT warnings outside the consumer:\n" + string.Join('\n', foreign));
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            reported.Select(warning => $"{warning.Line} {warning.Code}").Distinct().Order(StringComparer.Ordinal));

        var calledEntryPoints = reported
            .Where(warning => warning.Code == "IL2026")
            .Select(warning => Member.Match(warning.Message).Groups["member"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(AnnotatedPublicMembers().Unreferenced.Length, calledEntryPoints.Count);
    }

    private static (MemberInfo[] Unreferenced, MemberInfo[] Dynamic) AnnotatedPublicMembers()
    {
        var members = new[] { typeof(BinarySerializer).Assembly, typeof(BinarySerializerException).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Prepend(type))
            .ToArray();

        return (
            [.. members.Where(member => member.IsDefined(typeof(RequiresUnreferencedCodeAttribute), inherit: false))],
            [.. members.Where(member => member.IsDefined(typeof(RequiresDynamicCodeAttribute), inherit: false))]);
    }

    private static string Describe(MemberInfo member) =>
        member is MethodInfo method
            ? $"{method.DeclaringType!.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})"
            : $"{member.DeclaringType?.Name}.{member.Name}";

    /// <summary>
    /// Builds <paramref name="project"/> and everything it references into a directory of its own, so
    /// the packages are compiled afresh and report every warning, and nothing in the working tree is
    /// touched.
    /// </summary>
    private static (string File, int Line, string Code, string Message)[] Build(string project)
    {
        string artifacts = Path.Combine(Path.GetTempPath(), "viper-aot-" + Guid.NewGuid().ToString("N"));

        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = SourceTree.RepositoryRoot
        };

        foreach (string argument in new[]
                 {
                     "build", project,
                     "--configuration", Configuration,
                     "--artifacts-path", artifacts,
                     "--disable-build-servers",
                     "-nodeReuse:false",
                     "-consoleLoggerParameters:NoSummary"
                 })
            start.ArgumentList.Add(argument);

        foreach (string variable in start.Environment.Keys
                     .Where(name => name.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            start.Environment.Remove(variable);

        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The consumer build did not finish within five minutes.");
            }

            string log = output.Result + error.Result;
            Assert.True(process.ExitCode == 0, "The consumer build failed:\n" + log);

            return
            [
                .. log.Split('\n')
                    .Select(line => Diagnostic.Match(line.TrimEnd('\r').Trim()))
                    .Where(match => match.Success)
                    .Select(match => (
                        match.Groups["file"].Value,
                        int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                        match.Groups["code"].Value,
                        match.Groups["message"].Value))
                    .Distinct()
            ];
        }
        finally
        {
            try
            {
                Directory.Delete(artifacts, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
