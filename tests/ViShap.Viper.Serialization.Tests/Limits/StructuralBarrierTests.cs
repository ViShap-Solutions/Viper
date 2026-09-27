using System.Reflection;
using System.Runtime.CompilerServices;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-39, LIM-40, LIM-44, LIM-47, LIM-48 and LIM-50: the barriers that make the security checks
/// structural rather than conventional. None of them has a runtime symptom on its own — a bypass
/// changes nothing observable until the day it lets an unchecked value through — so the shape of the
/// code is asserted directly.
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
                "ViShap.Viper.Serialization/Io/WireReader.cs",
                "ViShap.Viper.Serialization/Io/WireWriter.cs"
            ],
            callers);
    }

    // --- LIM-47: a composite formatter cannot express a loop over an unchecked count -------------

    [Fact]
    public void CompositeReader_ExposesOnlyTheCheckedOperations()
    {
        // No raw integer read is on this list, so a composite has no way to obtain a loop bound
        // except as a validated ElementCount or ArrayShape.
        Assert.Equal(
            ["ReadCount", "ReadFlag", "ReadShape", "ReadValue"],
            DeclaredMethodNames(typeof(CompositeReader), BindingFlags.Instance));
    }

    [Fact]
    public void CompositeWriter_ExposesOnlyTheCheckedOperations()
    {
        Assert.Equal(
            ["WriteCount", "WriteFlag", "WriteShape", "WriteValue"],
            DeclaredMethodNames(typeof(CompositeWriter), BindingFlags.Instance));
    }

    [Fact]
    public void CompositeSurfaces_AreCreatedOnlyByTheEngineEntry()
    {
        // A formatter cannot build a surface around a reader or writer of its own: the constructor is
        // private, and the one static member is the entry the engine calls with the formatter.
        const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags statics =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var surface in new[] { typeof(CompositeReader), typeof(CompositeWriter) })
        {
            Assert.All(surface.GetConstructors(instance), constructor => Assert.True(
                constructor.IsPrivate, $"{surface.Name} has a non-private constructor."));

            var entry = Assert.Single(surface.GetMethods(statics));
            Assert.Equal(typeof(ICompositeFormatter), entry.GetParameters()[0].ParameterType);
        }
    }

    [Fact]
    public void CompositeFormatter_IsHandedTheSurfaceAndNeverTheEngine()
    {
        Type[] forbidden =
        [
            typeof(GraphReader), typeof(GraphWriter), typeof(WireReader), typeof(WireWriter)
        ];

        foreach (var method in typeof(ICompositeFormatter).GetMethods(AllDeclared))
        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;
            Assert.DoesNotContain(type.IsByRef ? type.GetElementType()! : type, forbidden);
        }
    }

    [Fact]
    public void CompositeFormatters_NeverNameTheEngineOrThePayloadPrimitives()
    {
        string[] forbidden = ["GraphReader", "GraphWriter", "WireReader", "WireWriter", "ReadInt32"];

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
        // The engine holds no reader or writer of its own: each call receives one by reference, so
        // there is nothing it could hand to a composite except through the surface.
        Assert.Null(typeof(GraphReader).GetProperty("Values", AllDeclared));
        Assert.Null(typeof(GraphWriter).GetProperty("Values", AllDeclared));

        Type[] wire = [typeof(WireReader), typeof(WireWriter)];
        foreach (var engine in new[] { typeof(GraphReader), typeof(GraphWriter) })
        {
            Assert.All(engine.GetFields(AllDeclared), field =>
                Assert.DoesNotContain(field.FieldType, wire));
            Assert.All(engine.GetProperties(AllDeclared), property =>
                Assert.DoesNotContain(property.PropertyType, wire));
            Assert.All(engine.GetMethods(AllDeclared), method =>
                Assert.DoesNotContain(method.ReturnType, wire));
        }
    }

    private static string[] DeclaredMethodNames(Type type, BindingFlags scope) =>
        [.. type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | scope)
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
    public void WireReader_HandsOutNoStream()
    {
        AssertNoStreamIsReachable(typeof(WireReader));
    }

    [Fact]
    public void WireWriter_HandsOutNoStream()
    {
        AssertNoStreamIsReachable(typeof(WireWriter));
    }

    [Fact]
    public void Slice_YieldsAReaderBoundedToTheDeclaredField()
    {
        // Slice is the single member that yields a reader over part of the payload, and the reader
        // it yields ends where the field ends, so a decoder inside it cannot reach the next field.
        Assert.Equal(typeof(WireReader), typeof(WireReader).GetMethod(nameof(WireReader.Slice))!.ReturnType);

        var ex = Record.Exception(() =>
        {
            var reader = new WireReader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Operation());
            var field = reader.Slice(2, "Field");
            Assert.Equal(6, reader.Remaining);
            Assert.Equal(2, field.Remaining);
            field.ReadInt32();
        });

        Assert.IsType<BinaryFormatException>(ex);
    }

    // --- LIM-48: the payload primitives live on two ref structs and nowhere else ----------------

    [Fact]
    public void WireReaderAndWireWriter_AreRefStructs()
    {
        // A ref struct cannot be stored on the heap, captured by a lambda or held across an await,
        // so a reader or writer cannot outlive the call that was handed it.
        Assert.True(typeof(WireReader).IsByRefLike);
        Assert.True(typeof(WireWriter).IsByRefLike);
    }

    [Fact]
    public void OnlyWireReaderAndWireWriter_DeclarePayloadPrimitives()
    {
        string[] primitives =
        [
            "Boolean", "Byte", "SByte", "Int16", "UInt16", "Char", "Int32", "UInt32", "Int64",
            "UInt64", "Single", "Double", "Decimal", "7BitEncodedInt", "String", "Blob", "BitCount"
        ];

        var names = primitives
            .SelectMany(primitive => new[] { $"Read{primitive}", $"Write{primitive}" })
            .ToHashSet(StringComparer.Ordinal);

        var declaring = typeof(BinarySerializer).Assembly
            .GetTypes()
            .Where(type => type.GetMethods(AllDeclared).Any(method =>
                names.Contains(method.Name) && method.GetBaseDefinition().DeclaringType != typeof(Stream)))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(WireReader), nameof(WireWriter)], declaring);
    }

    // --- LIM-50: the engine never awaits ---------------------------------------------------------

    [Fact]
    public void TheEngineAndTheFormatters_DeclareNoAsynchronousMethod()
    {
        // Waiting for bytes happens at the frame edge. A traversal that could await would hold a
        // budget, a depth scope and a reference table across a suspension it does not control.
        string[] layers = ["ViShap.Viper.Engine", "ViShap.Viper.Formatters"];

        var offenders = typeof(BinarySerializer).Assembly
            .GetTypes()
            .Where(type => layers.Any(layer => type.Namespace?.StartsWith(layer, StringComparison.Ordinal) == true))
            .SelectMany(type => type.GetMethods(AllDeclared).Select(method => (type, method)))
            .Where(pair =>
                pair.method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null ||
                pair.method.GetCustomAttribute<AsyncIteratorStateMachineAttribute>() is not null ||
                IsAwaitable(pair.method.ReturnType))
            .Select(pair => $"{pair.type.Name}.{pair.method.Name}")
            .ToArray();

        Assert.True(offenders.Length == 0, $"Asynchronous methods below the pipeline: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheAsynchronousMethodCheck_RecognizesAnAwaitableReturn()
    {
        Assert.True(IsAwaitable(typeof(ValueTask<int>)));
        Assert.True(IsAwaitable(typeof(Task)));
        Assert.True(IsAwaitable(typeof(IAsyncEnumerable<int>)));
        Assert.False(IsAwaitable(typeof(int)));
    }

    private static bool IsAwaitable(Type type) =>
        typeof(Task).IsAssignableFrom(type) ||
        type == typeof(ValueTask) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>) ||
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>);

    private static SerializationOperation Operation() =>
        new(SerializationLimits.Default, keys: null, preserveReferences: false,
            requireEncryption: false, requireChecksum: false);

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
