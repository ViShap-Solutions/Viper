using System.Text.RegularExpressions;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Exceptions;

/// <summary>
/// Pins EXC-21, EXC-22 and CFG-09: invariants with no runtime symptom. A blanket catch or a policy
/// check at the wrong layer changes nothing observable until the day it hides a real failure, so the
/// shape of the source is asserted directly.
/// </summary>
public partial class SourceInvariantTests
{
    [Fact]
    public void SourceTree_IsActuallyFound()
    {
        // Guards the rest of this suite: an invariant that silently checks nothing always passes.
        Assert.Contains("ViShap.Viper.Serialization/BinarySerializer.cs", SourceTree.ProductionFiles.Keys);
        Assert.True(SourceTree.ProductionFiles.Count > 40);
    }

    [Fact]
    public void Production_ContainsNoBareRethrowOfViperExceptions()
    {
        // A catch that performs real cleanup before rethrowing is fine; one that only rethrows is
        // noise that hides where a failure was actually handled.
        var offenders = SourceTree.ProductionFiles
            .Where(file => BareRethrow().IsMatch(file.Value))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Bare `catch (BinarySerializerException) {{ throw; }}` in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Production_NeverCatchesIoExceptionOutsideAStreamBoundary()
    {
        // I/O is attributed where the stream is touched. Wrapping a whole codec operation would
        // relabel a format or limit failure as a stream failure.
        string[] allowed =
        [
            "ViShap.Viper.Serialization/Security/MeteredReadStream.cs",
            "ViShap.Viper.Serialization/Security/MeteredWriteStream.cs",
            "ViShap.Viper.Serialization/Security/WindowReadStream.cs",
            "ViShap.Viper.Serialization/Io/ValueReader.cs",
            "ViShap.Viper.Serialization/Io/ValueWriter.cs",
            "ViShap.Viper.Serialization/Pipeline/FormatRouter.cs",
            "ViShap.Viper.Serialization/Metadata/BinaryFormatInspector.cs"
        ];

        var offenders = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("catch (IOException", StringComparison.Ordinal))
            .Select(file => file.Key)
            .Except(allowed, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"`catch (IOException)` outside a stream boundary in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Production_ContainsNoCatchAllExceptionHandler()
    {
        // A filtered catch names the exceptions it expects. An unfiltered one reaches `{` directly
        // and would turn any bug into a Viper exception.
        var offenders = SourceTree.ProductionFiles
            .Where(file => UnfilteredCatchAll().IsMatch(file.Value))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Unfiltered `catch (Exception)` in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void LimitValidation_HappensOnlyAtAConfigurationBoundary()
    {
        // Limits are validated when a configuration is built or accepted, never per operation.
        string[] configurationBoundaries =
        [
            "ViShap.Viper.Serialization/BinarySerializer.cs",
            "ViShap.Viper.Serialization/Configuration/BinarySerializerOptions.cs",
            "ViShap.Viper.Serialization/Configuration/BinarySerializerOptionsBuilder.cs",
            "ViShap.Viper.Serialization/Metadata/BinaryFormatInspector.cs",
            "ViShap.Viper.Serialization/Security/SerializationLimits.cs"
        ];

        var callers = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains(".Validate()", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.All(callers, caller => Assert.Contains(caller, configurationBoundaries));
    }

    [Fact]
    public void NoTypeBelowThePipelineReferencesSerializationLimits()
    {
        string[] below = ["Engine/", "Formatters/", "Io/", "Cache/", "Compression/", "Checksum/", "Crypto/"];

        var offenders = SourceTree.ProductionFiles
            .Where(file => below.Any(folder =>
                file.Key.Contains($"ViShap.Viper.Serialization/{folder}", StringComparison.Ordinal)))
            .Where(file => file.Value.Contains("SerializationLimits", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"`SerializationLimits` referenced below the pipeline in: {string.Join(", ", offenders)}");
    }

    [GeneratedRegex(@"catch\s*\(\s*BinarySerializerException\s*\)\s*\{\s*throw\s*;\s*\}", RegexOptions.Singleline)]
    private static partial Regex BareRethrow();

    [GeneratedRegex(@"catch\s*\(\s*Exception(\s+\w+)?\s*\)\s*\{", RegexOptions.Singleline)]
    private static partial Regex UnfilteredCatchAll();
}
