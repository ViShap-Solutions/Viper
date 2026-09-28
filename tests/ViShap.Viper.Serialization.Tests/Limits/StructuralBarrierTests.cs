using System.Reflection;
using System.Runtime.CompilerServices;
using ViShap.Viper.Engine;
using ViShap.Viper.Formatters;
using ViShap.Viper.Io;
using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-39, LIM-40, LIM-44, LIM-47, LIM-48, LIM-49, LIM-50 and LIM-51: the barriers that make the security checks
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
                "ViShap.Viper.Serialization/Engine/Codecs/CompositeCodec.cs",
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
        // except as a validated ArrayShape, and the elements behind one are read by the engine.
        Assert.Equal(
            ["ReadElements", "ReadShape", "ReadValue"],
            DeclaredMethodNames(typeof(CompositeReader), BindingFlags.Instance));
    }

    [Fact]
    public void CompositeWriter_ExposesOnlyTheCheckedOperations()
    {
        Assert.Equal(
            ["WriteElements", "WriteShape", "WriteValue"],
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
            Assert.Equal(typeof(ICompositeFormatter<>), entry.GetParameters()[0].ParameterType.GetGenericTypeDefinition());
        }
    }

    [Fact]
    public void CompositeFormatter_IsHandedTheSurfaceAndNeverTheEngine()
    {
        Type[] forbidden =
        [
            typeof(WireReader), typeof(WireWriter), typeof(OperationState), typeof(MemberReader),
            typeof(MemberWriter)
        ];

        foreach (var method in typeof(ICompositeFormatter<>).GetMethods(AllDeclared))
        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;
            Assert.DoesNotContain(type.IsByRef ? type.GetElementType()! : type, forbidden);
        }
    }

    [Fact]
    public void CompositeFormatters_NeverNameTheEngineOrThePayloadPrimitives()
    {
        string[] forbidden = ["WireReader", "WireWriter", "OperationState", "ReadInt32", "ReadCount", "ElementCount"];

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
    public void TheCodecs_HoldNoStateOfAnOperation()
    {
        // A codec and a contract are built once per type and shared by every operation, so a field of
        // either that held a budget, a traversal or a reader would carry one call into the next.
        Type[] forbidden =
        [
            typeof(OperationState), typeof(SerializationBudget), typeof(GraphState), typeof(PhaseBudget),
            typeof(WriteReferenceTable), typeof(ReadReferenceTable)
        ];

        var shared = typeof(BinarySerializer).Assembly
            .GetTypes()
            .Where(type => IsCodecOrContract(type))
            .ToArray();

        Assert.NotEmpty(shared);
        Assert.All(shared, type => Assert.All(type.GetFields(AllDeclared), field =>
            Assert.DoesNotContain(field.FieldType, forbidden)));
    }

    private static bool IsCodecOrContract(Type type)
    {
        for (var level = type; level is not null; level = level.BaseType)
        {
            var definition = level.IsGenericType ? level.GetGenericTypeDefinition() : level;
            if (definition == typeof(Codec<>) || definition == typeof(TypeContract))
                return true;
        }

        return false;
    }

    // --- LIM-49: shapes and contracts receive no count and no primitive --------------------------

    [Fact]
    public void Shapes_TakeNoCountAndNoPrimitive()
    {
        // A shape counts, enumerates, builds and completes. The count read from the wire, the loop
        // and every primitive belong to the engine's codec, so no shape method can take one.
        Type[] forbidden =
        [
            typeof(WireReader), typeof(WireWriter), typeof(ElementCount), typeof(OperationState),
            typeof(CompositeReader), typeof(CompositeWriter), typeof(MemberReader), typeof(MemberWriter)
        ];

        Type[] shapes = [typeof(ISequenceShape<,,,>), typeof(IMapShape<,,,,>), typeof(IArrayShape<,>)];

        foreach (var shape in shapes)
        foreach (var method in shape.GetMethods(AllDeclared))
        {
            Assert.DoesNotContain(Unwrapped(method.ReturnType), forbidden);
            Assert.All(method.GetParameters(), parameter =>
                Assert.DoesNotContain(Unwrapped(parameter.ParameterType), forbidden));
        }
    }

    [Fact]
    public void TypeContract_IsHandedOnlyTheMemberSurfaces()
    {
        // What a contract implements — creation, writing, reading, the response to a key — takes a
        // MemberWriter or a MemberReader and nothing that reaches bytes, counts or a position.
        var abstracts = typeof(TypeContract<>)
            .GetMethods(AllDeclared)
            .Where(method => method.IsAbstract)
            .ToArray();

        Assert.Equal(
            ["Create", "Read", "ReadField", "Write"],
            abstracts.Select(method => method.Name).Order(StringComparer.Ordinal));

        Type[] forbidden = [typeof(WireReader), typeof(WireWriter), typeof(ElementCount), typeof(OperationState)];
        Assert.All(abstracts, method => Assert.All(method.GetParameters(), parameter =>
            Assert.DoesNotContain(Unwrapped(parameter.ParameterType), forbidden)));
    }

    [Fact]
    public void MemberSurfaces_ExposeOnlyMemberValues()
    {
        // No bytes, no counts, no position: what a contract can call is one value at a time.
        Assert.Equal(["Field", "Member"], PublicMemberNames(typeof(MemberWriter)));
        Assert.Equal(["Member", "Value"], PublicMemberNames(typeof(MemberReader)));

        foreach (var surface in new[] { typeof(MemberWriter), typeof(MemberReader) })
        {
            Assert.All(
                surface.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                constructor => Assert.True(constructor.IsPrivate, $"{surface.Name} has a non-private constructor."));
        }
    }

    [Fact]
    public void OnlyTheEngineReadsACountFromTheWire()
    {
        // ReadCount and WriteCount are the only way to a count, and outside the readers and writers
        // that declare them only the engine's codecs call them.
        var callers = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("ReadCount(", StringComparison.Ordinal) ||
                           file.Value.Contains("WriteCount(", StringComparison.Ordinal))
            .Select(file => file.Key)
            .Where(file => !file.StartsWith("ViShap.Viper.Serialization/Io/", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(callers);
        Assert.All(callers, caller => Assert.StartsWith(
            "ViShap.Viper.Serialization/Engine/Codecs/", caller, StringComparison.Ordinal));
    }

    [Fact]
    public void Shapes_NeverNameThePayloadPrimitivesOrACount()
    {
        string[] forbidden = ["WireReader", "WireWriter", "ElementCount", "ReadCount", "OperationState"];
        string[] folders = ["Formatters/Sequences/", "Formatters/Maps/"];

        var files = SourceTree.ProductionFiles
            .Where(file => folders.Any(folder =>
                file.Key.Contains($"ViShap.Viper.Serialization/{folder}", StringComparison.Ordinal)))
            .ToArray();

        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => forbidden.Any(name => file.Value.Contains(name, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ToArray();

        Assert.True(offenders.Length == 0, $"A shape reaches the wire in: {string.Join(", ", offenders)}");
    }

    private static Type Unwrapped(Type type) => type.IsByRef ? type.GetElementType()! : type;

    private static string[] PublicMemberNames(Type type) =>
        [.. type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member is not ConstructorInfo)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)];

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
            typeof(IScalarFormatter<>), typeof(ISequenceShape<,,,>), typeof(IMapShape<,,,,>),
            typeof(IArrayShape<,>), typeof(ICompositeFormatter<>)
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
            var reader = new WireReader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ref Operation().State);
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

    private static OperationBox Operation() => new();

    // --- LIM-51: one operation state per public call -------------------------------------------

    [Fact]
    public void OperationState_IsCreatedOnlyAtThePublicEdge()
    {
        // The serializer creates the state of each call once, and the inspector — which reads a
        // header outside any call — creates its own. Nothing else creates one, and nothing but the
        // state creates a budget or a phase policy.
        Assert.Equal(
            [
                "ViShap.Viper.Serialization/BinarySerializer.cs",
                "ViShap.Viper.Serialization/Metadata/BinaryFormatInspector.cs"
            ],
            FilesContaining("new OperationState(", "private OperationState BeginOperation() =>"));

        Assert.Equal(
            ["ViShap.Viper.Serialization/Security/OperationState.cs"],
            FilesContaining("new SerializationBudget(", "new PhaseBudget("));
    }

    [Fact]
    public void OperationState_TravelsByReference()
    {
        // A copy of the state would account for the call twice, so every member that takes one
        // below the public edge takes it by reference; only an asynchronous method, which cannot,
        // takes the one copy it then owns.
        var takers = typeof(BinarySerializer).Assembly
            .GetTypes()
            .Where(type => type != typeof(BinarySerializer))
            .SelectMany(type => type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)))
            .SelectMany(method => method.GetParameters().Select(parameter => (method, parameter)))
            .Where(pair => Unwrapped(pair.parameter.ParameterType) == typeof(OperationState))
            .ToArray();

        Assert.NotEmpty(takers);
        Assert.All(takers, pair => Assert.True(
            pair.parameter.ParameterType.IsByRef ||
            pair.method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null,
            $"{pair.method.DeclaringType!.Name}.{pair.method.Name} takes the operation state by value."));
    }

    private static string[] FilesContaining(params string[] fragments) =>
        [.. SourceTree.ProductionFiles
            .Where(file => fragments.Any(fragment => file.Value.Contains(fragment, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .Order(StringComparer.Ordinal)];

    // --- LIM-39: the phase policy belongs to the pipeline, not to an algorithm -------------------

    [Fact]
    public void PhaseBudget_IsNeverReachedFromAnAlgorithm()
    {
        string[] algorithmFolders =
        [
            "Algorithms/", "Compression/", "Checksum/", "Crypto/", "Formatters/"
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
