using System.Reflection;
using System.Text.RegularExpressions;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-52…LIM-59: the invariants of contract §25 whose other checkpoints observe a behaviour, held
/// here by the shape of the code — the loop bound of a keyed object, the algorithm registry, the union
/// tag, the one encoding of a string, the exception taxonomy, key ownership, the associated data, the
/// single write to a destination, and the one payload encoder of both format versions.
/// </summary>
public partial class InvariantStructureTests
{
    private const string Serialization = "ViShap.Viper.Serialization/";

    private static string Source(string path) => SourceTree.ProductionFiles[Serialization + path];

    private static IEnumerable<KeyValuePair<string, string>> Under(string folder) =>
        SourceTree.ProductionFiles.Where(file => file.Key.StartsWith(Serialization + folder, StringComparison.Ordinal));

    // --- LIM-52 (INV-4): the keyed field loop is bounded by a validated count -----------------------

    [Fact]
    public void KeyedObject_TakesItsFieldCountAsAValidatedCount()
    {
        string codec = Source("Engine/Codecs/ObjectCodec.cs");

        Assert.Contains("reader.ReadCount(CountKind.KeyedFields", codec, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadFolded(", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatters_ReadNoRawCount()
    {
        string[] raw = ["ReadFolded(", "Read7BitEncodedInt(", "ReadCount("];

        var offenders = Under("Formatters/")
            .Where(file => raw.Any(call => file.Value.Contains(call, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ToArray();

        Assert.Empty(offenders);
    }

    // --- LIM-53 (INV-7): no process-wide registry of algorithms -------------------------------------

    [Fact]
    public void NoStaticField_HoldsAnAlgorithmOrAFactoryOfOne()
    {
        Type[] families = [typeof(ICompressionAlgorithm), typeof(IChecksumAlgorithm), typeof(IEncryptionAlgorithm)];

        bool Mentions(Type type) =>
            families.Any(family => family.IsAssignableFrom(type)) ||
            (type.IsGenericType && type.GetGenericArguments().Any(Mentions)) ||
            (type.IsArray && Mentions(type.GetElementType()!));

        var offenders = new[] { typeof(BinarySerializer).Assembly, typeof(IEncryptionAlgorithm).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => !field.IsLiteral && Mentions(field.FieldType))
            .Select(field => $"{field.DeclaringType}.{field.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    // --- LIM-54 (INV-8): no type name reaches the wire ----------------------------------------------

    [Fact]
    public void NoProductionFile_ResolvesOrWritesATypeByName()
    {
        string[] forbidden = ["AssemblyQualifiedName", "Type.GetType(", "Assembly.Load", ".GetType(name"];

        var offenders = SourceTree.ProductionFiles
            .Where(file => forbidden.Any(call => file.Value.Contains(call, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void WireWriter_TakesNoType()
    {
        var parameters = typeof(Io.WireWriter)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.ParameterType == typeof(Type));

        Assert.Empty(parameters);
    }

    // --- LIM-55 (INV-9): the payload's strings have one encoder in each direction -------------------

    [Fact]
    public void WireReaderAndWriter_UseOnlyTheStrictEncoding()
    {
        foreach (string file in new[] { "Io/WireReader.cs", "Io/WireWriter.cs" })
        {
            string source = Source(file);

            Assert.Contains("throwOnInvalidBytes: true", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding.UTF8", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoFileBelowThePipeline_DecodesOrEncodesTextItself()
    {
        string[] folders = ["Engine/", "Formatters/", "Io/"];

        var offenders = folders
            .SelectMany(Under)
            .Where(file => file.Value.Contains("Encoding.UTF8", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.Empty(offenders);
    }

    // --- LIM-56 (INV-10): only the taxonomy and the standard exceptions of §8.10 are thrown ----------

    [GeneratedRegex(@"throw new (\w+)")]
    private static partial Regex Throw();

    [Fact]
    public void ProductionCode_ThrowsOnlyTheTaxonomyAndTheStandardExceptions()
    {
        string[] standard =
        [
            nameof(ArgumentNullException), nameof(ArgumentException), nameof(ArgumentOutOfRangeException),
            nameof(NotSupportedException), nameof(ObjectDisposedException), nameof(OperationCanceledException)
        ];

        var taxonomy = typeof(BinarySerializerException).Assembly.GetTypes()
            .Where(type => typeof(BinarySerializerException).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToHashSet();

        // An algorithm primitive may fail under its own name; the service that calls it translates.
        string[] primitives = ["Compression/", "Checksum/", "Crypto/"];

        var offenders = SourceTree.ProductionFiles
            .Where(file => !primitives.Any(folder => file.Key.StartsWith(Serialization + folder, StringComparison.Ordinal)))
            .SelectMany(file => Throw().Matches(file.Value).Select(match => (file.Key, Type: match.Groups[1].Value)))
            .Where(thrown => !taxonomy.Contains(thrown.Type) && !standard.Contains(thrown.Type))
            .Select(thrown => $"{thrown.Key}: {thrown.Type}")
            .ToArray();

        Assert.Empty(offenders);
    }

    // --- LIM-57 (INV-11): key material is an owned copy, cleared only by its owner ------------------

    [Fact]
    public void SecretKey_IsCreatedOnlyByCopying()
    {
        Assert.Empty(typeof(SecretKey).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var factories = typeof(SecretKey)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(SecretKey))
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal(["CopyFrom"], factories);
        Assert.DoesNotContain(
            typeof(SecretKey).GetProperties(),
            property => property.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void KeyMaterial_IsZeroedOnlyWhereItIsOwned()
    {
        var zeroing = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("ZeroMemory", StringComparison.Ordinal))
            .Select(file => file.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["ViShap.Viper.Core/Crypto/SecretKey.cs", "ViShap.Viper.Serialization/Crypto/KeyProviders.cs"],
            zeroing);
    }

    // --- LIM-58 (INV-14, INV-15): the header is the associated data; a frame is written once --------

    [Fact]
    public void Encryption_TakesTheHeaderBytesAsItsAssociatedData()
    {
        string pipeline = Source("Pipeline/V1FormatPipeline.cs");

        Assert.Contains("header.CopyTo(rented);", pipeline, StringComparison.Ordinal);
        Assert.Contains("phases.Encryption, onDisk, headerBytes,", pipeline, StringComparison.Ordinal);

        var callers = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("new SealedBody(", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ToArray();

        Assert.Equal([Serialization + "Pipeline/V1FormatPipeline.cs"], callers);
    }

    [Fact]
    public void OnlyTheFinishedFrame_WritesToTheCallersDestination()
    {
        string[] writes = ["destination.Write(", "destination.GetSpan(", "destination.GetMemory(", "destination.Advance("];

        var writers = SourceTree.ProductionFiles
            .Where(file => writes.Any(call => file.Value.Contains(call, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .Where(key => !key.Contains("/Compression/", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        // The payload buffer copies itself out only when the finished frame asks it to.
        Assert.Equal([Serialization + "Io/PayloadBuffer.cs", Serialization + "Pipeline/EncodedFrame.cs"], writers);
    }

    // --- LIM-59 (INV-18): V0 and V1 encode the payload through the one engine entry -----------------

    [Fact]
    public void BothVersions_EncodeThePayloadThroughTheOneEngineEntry()
    {
        string v0 = Source("Pipeline/V0FormatPipeline.cs");
        string v1 = Source("Pipeline/V1FormatPipeline.cs");

        Assert.Contains("Graph.WriteRoot(ref writer, data, preserveReferences: false);", v0, StringComparison.Ordinal);
        Assert.Contains("Graph.WriteRoot(ref writer, data, state.PreserveReferences);", v1, StringComparison.Ordinal);

        var entries = SourceTree.ProductionFiles
            .Where(file => file.Value.Contains("Graph.WriteRoot(", StringComparison.Ordinal))
            .Select(file => file.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(entries, key => key.Contains("/Engine/", StringComparison.Ordinal) || key.Contains("/Formatters/", StringComparison.Ordinal));
    }
}
