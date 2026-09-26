using System.Reflection;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-39, LIM-40 and LIM-44: the barriers that make the security checks structural rather than
/// conventional. None of them has a runtime symptom on its own — a bypass changes nothing observable
/// until the day it lets an unchecked value through — so the shape of the code is asserted directly.
/// </summary>
public class StructuralBarrierTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    // --- LIM-40: a count exists only as a validated one ------------------------------------------

    [Fact]
    public void ElementCount_HasNoAccessibleConstructor()
    {
        var constructors = typeof(ElementCount).GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.All(constructors, constructor => Assert.True(
            constructor.IsPrivate,
            $"ElementCount exposes a {(constructor.IsPublic ? "public" : "non-private")} constructor."));
    }

    [Fact]
    public void ElementCount_IsProducedOnlyByTheValidatingFactories()
    {
        var factories = typeof(ElementCount)
            .GetMethods(AllDeclared)
            .Where(method => method.ReturnType == typeof(ElementCount))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Validate", "ValidateShape"], factories);
    }

    [Fact]
    public void ElementCount_HasNoWritableValue()
    {
        var value = typeof(ElementCount).GetProperty(nameof(ElementCount.Value))!;

        Assert.False(value.CanWrite);
    }

    [Fact]
    public void ElementCount_DefaultInstance_IsTheEmptyCount()
    {
        // A struct can always be default-constructed; the default has to be the harmless value.
        Assert.Equal(0, default(ElementCount).Value);
    }

    [Fact]
    public void Validate_IsTheOnlyPlaceACountIsCheckedAndCharged()
    {
        // The engine reads and writes a count through ReadCount / WriteCount, which forward to
        // Validate. The one other caller is the composite surface, which validates a
        // multidimensional shape as a whole — still inside the engine, and never in a formatter.
        var callers = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("ElementCount.Validate", StringComparison.Ordinal))
            .Select(file => file.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "ViShap.Viper.Serialization/Engine/CompositeSurface.cs",
                "ViShap.Viper.Serialization/Io/ValueReader.cs",
                "ViShap.Viper.Serialization/Io/ValueWriter.cs"
            ],
            callers);
    }

    // --- a composite formatter cannot express a loop over an unchecked count ----------------------

    [Fact]
    public void CompositeReader_ExposesOnlyTheCheckedOperations()
    {
        // No raw integer read is on this list, so a composite has no way to obtain a loop bound
        // except as a validated ElementCount or ArrayShape.
        Assert.Equal(
            ["ReadCount", "ReadFlag", "ReadShape", "ReadValue"],
            DeclaredMethodNames(typeof(CompositeReader)));
    }

    [Fact]
    public void CompositeWriter_ExposesOnlyTheCheckedOperations()
    {
        Assert.Equal(
            ["WriteCount", "WriteFlag", "WriteShape", "WriteValue"],
            DeclaredMethodNames(typeof(CompositeWriter)));
    }

    [Fact]
    public void CompositeFormatter_IsHandedTheSurfaceAndNeverTheEngine()
    {
        Type[] forbidden =
        [
            typeof(GraphReader), typeof(GraphWriter), typeof(ValueReader), typeof(ValueWriter)
        ];

        foreach (var method in typeof(ICompositeFormatter).GetMethods(AllDeclared))
            Assert.All(method.GetParameters(), parameter => Assert.DoesNotContain(
                parameter.ParameterType, forbidden));
    }

    [Fact]
    public void CompositeFormatters_NeverNameTheEngineOrThePayloadPrimitives()
    {
        string[] forbidden = ["GraphReader", "GraphWriter", "ValueReader", "ValueWriter", "ReadInt32"];

        var offenders = SourceTree.ProductionFiles
            .Where(file => file.Key.Contains(
                "ViShap.Viper.Serialization/Formatters/Composites/", StringComparison.Ordinal))
            .Where(file => forbidden.Any(name => file.Value.Contains(name, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"A composite formatter reaches past its surface in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheEngine_HandsOutNoPayloadPrimitives()
    {
        // The engine once exposed its ValueReader and ValueWriter so composites could reach them;
        // with the surface in place nothing needs to, and nothing may.
        Assert.Null(typeof(GraphReader).GetProperty("Values", AllDeclared));
        Assert.Null(typeof(GraphWriter).GetProperty("Values", AllDeclared));
    }

    private static string[] DeclaredMethodNames(Type type) =>
        [.. type.GetMethods(AllDeclared)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)];

    // --- LIM-44: payload bytes are reachable only through the readers and writers ----------------

    [Fact]
    public void Formatters_NeverNameAStream()
    {
        var offenders = SourceTree.ProductionFiles
            .Where(file => file.Key.Contains(
                "ViShap.Viper.Serialization/Formatters/", StringComparison.Ordinal))
            .Where(file => file.Value.Contains("Stream", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"A formatter reaches for a stream in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void FormatterInterfaces_TakeAndReturnNoStream()
    {
        Type[] interfaces =
        [
            typeof(ITypeFormatter), typeof(IScalarFormatter), typeof(ISequenceFormatter),
            typeof(IMapFormatter), typeof(ICompositeFormatter)
        ];

        foreach (var contract in interfaces)
        foreach (var method in contract.GetMethods(AllDeclared))
        {
            Assert.False(
                typeof(Stream).IsAssignableFrom(method.ReturnType),
                $"{contract.Name}.{method.Name} returns a stream.");

            Assert.All(method.GetParameters(), parameter => Assert.False(
                typeof(Stream).IsAssignableFrom(parameter.ParameterType),
                $"{contract.Name}.{method.Name} takes a stream."));
        }
    }

    [Fact]
    public void ValueReader_HandsOutNoStream()
    {
        AssertNoStreamIsReachable(typeof(ValueReader));
    }

    [Fact]
    public void ValueWriter_HandsOutNoStream()
    {
        AssertNoStreamIsReachable(typeof(ValueWriter));
    }

    [Fact]
    public void PayloadWindow_ExposesOnlyABoundedStream()
    {
        // OpenWindow is the single member that yields a stream at all, and the stream it yields
        // knows its own boundary, so a decoder inside it still cannot reach the caller's source.
        var stream = typeof(PayloadWindow).GetProperty(nameof(PayloadWindow.Stream))!;

        Assert.Equal(typeof(WindowReadStream), stream.PropertyType);
    }

    // --- LIM-39: the phase policy belongs to the pipeline, not to an algorithm -------------------

    [Fact]
    public void PhaseBudget_IsNeverReachedFromAnAlgorithm()
    {
        string[] algorithmFolders =
        [
            "Algorithms/", "Compression/", "Checksum/", "Crypto/", "Formatters/", "Cache/"
        ];

        var offenders = SourceTree.ProductionFiles
            .Where(file => algorithmFolders.Any(folder =>
                file.Key.Contains($"ViShap.Viper.Serialization/{folder}", StringComparison.Ordinal)))
            .Where(file => file.Value.Contains("PhaseBudget", StringComparison.Ordinal)
                        || file.Value.Contains(".Phases", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"A phase size policy is consulted below the pipeline in: {string.Join(", ", offenders)}");
    }

    private static void AssertNoStreamIsReachable(Type type)
    {
        foreach (var property in type.GetProperties(AllDeclared))
            Assert.False(
                typeof(Stream).IsAssignableFrom(property.PropertyType),
                $"{type.Name}.{property.Name} exposes the underlying stream.");

        foreach (var method in type.GetMethods(AllDeclared))
            Assert.False(
                typeof(Stream).IsAssignableFrom(method.ReturnType),
                $"{type.Name}.{method.Name} returns the underlying stream.");

        foreach (var field in type.GetFields(AllDeclared).Where(field => !field.IsPrivate))
            Assert.False(
                typeof(Stream).IsAssignableFrom(field.FieldType),
                $"{type.Name}.{field.Name} exposes the underlying stream.");
    }
}
