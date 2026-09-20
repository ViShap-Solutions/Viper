using System.Reflection;
using System.Xml.Linq;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins EXT-01, EXT-04 and EXT-05: the compiled public surface is exactly the one contract §3 lists,
/// and every member of it is documented. A type that appears here without appearing there is an
/// unannounced API addition; one that disappears is a break. Either way the contract and the assembly
/// must be changed together.
/// </summary>
public class PublicSurfaceTests
{
    /// <summary>Every public type contract §3 declares, by namespace-qualified name.</summary>
    private static readonly string[] Documented =
    [
        "ViShap.Viper.BinarySerializer",
        "ViShap.Viper.BinarySerializerOptions",
        "ViShap.Viper.BinarySerializerOptionsBuilder",
        "ViShap.Viper.StreamExtensions",
        "ViShap.Viper.BinaryContractAttribute",
        "ViShap.Viper.BinaryKeyAttribute",
        "ViShap.Viper.BinaryIgnoreAttribute",
        "ViShap.Viper.BinaryIncludeAttribute",
        "ViShap.Viper.BinaryOrderAttribute",
        "ViShap.Viper.BinaryUnionAttribute",

        "ViShap.Viper.Security.SerializationLimits",

        "ViShap.Viper.Compression.CompressionAlgorithm",
        "ViShap.Viper.Compression.ICompressionAlgorithm",
        "ViShap.Viper.Compression.NoCompression",
        "ViShap.Viper.Compression.Deflate",
        "ViShap.Viper.Compression.Brotli",

        "ViShap.Viper.Checksum.ChecksumAlgorithm",
        "ViShap.Viper.Checksum.IChecksumAlgorithm",
        "ViShap.Viper.Checksum.NoChecksum",
        "ViShap.Viper.Checksum.Crc32",

        "ViShap.Viper.Crypto.EncryptionAlgorithm",
        "ViShap.Viper.Crypto.IEncryptionAlgorithm",
        "ViShap.Viper.Crypto.NoEncryption",
        "ViShap.Viper.Crypto.Aes256Gcm",
        "ViShap.Viper.Crypto.SecretKey",
        "ViShap.Viper.Crypto.IKeyProvider",
        "ViShap.Viper.Crypto.StaticKeyProvider",
        "ViShap.Viper.Crypto.DelegateKeyProvider",

        "ViShap.Viper.Metadata.BinaryHeaderInfo",
        "ViShap.Viper.Metadata.BinaryFormatInspector",

        "ViShap.Viper.Diagnostics.BinaryFormatDumper",

        "ViShap.Viper.Exceptions.BinarySerializerException",
        "ViShap.Viper.Exceptions.BinaryConfigurationException",
        "ViShap.Viper.Exceptions.BinaryFormatException",
        "ViShap.Viper.Exceptions.BinaryLimitException",
        "ViShap.Viper.Exceptions.BinaryFormatNotSupportedException",
        "ViShap.Viper.Exceptions.BinaryIntegrityException",
        "ViShap.Viper.Exceptions.BinaryEncryptionException",
        "ViShap.Viper.Exceptions.BinaryEncryptionKeyException",
        "ViShap.Viper.Exceptions.BinaryStreamException",
        "ViShap.Viper.Exceptions.BinaryTypeException"
    ];

    private static string[] ActualSurface() =>
        [.. new[] { typeof(BinarySerializer).Assembly, typeof(BinarySerializerException).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsNested)
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void PublicSurface_ContainsNothingBeyondTheContract()
    {
        string[] undocumented = [.. ActualSurface().Except(Documented, StringComparer.Ordinal)];

        Assert.True(
            undocumented.Length == 0,
            $"Public types absent from System-Contract.md §3: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void PublicSurface_ContainsEverythingTheContractPromises()
    {
        string[] missing = [.. Documented.Except(ActualSurface(), StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0,
            $"Types promised by System-Contract.md §3 but not exported: {string.Join(", ", missing)}");
    }

    [Fact]
    public void PublicSurface_ExposesNoNestedPublicTypes()
    {
        var nested = new[] { typeof(BinarySerializer).Assembly, typeof(BinarySerializerException).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsNested)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(nested);
    }

    [Fact]
    public void EngineTypes_AreNotPublic()
    {
        // Each of these enforces part of the resource policy or the traversal protocol; publishing
        // any would let a caller step around it.
        string[] internalNames =
        [
            "ViShap.Viper.Engine.GraphReader",
            "ViShap.Viper.Engine.GraphWriter",
            "ViShap.Viper.Engine.TypeContract",
            "ViShap.Viper.Io.ValueReader",
            "ViShap.Viper.Io.ValueWriter",
            "ViShap.Viper.Io.ElementCount",
            "ViShap.Viper.Security.SerializationBudget",
            "ViShap.Viper.Security.SerializationOperation",
            "ViShap.Viper.Security.MeteredReadStream",
            "ViShap.Viper.Formatters.ITypeFormatter",
            "ViShap.Viper.Pipeline.FormatRouter"
        ];

        var exported = ActualSurface();

        Assert.All(internalNames, name => Assert.DoesNotContain(name, exported));
    }

    // --- EXT-04: every public member carries XML documentation ----------------------------------

    [Fact]
    public void EveryPublicMember_IsDocumented()
    {
        // CS1591 is a warning, so this is what holds the line: neither package may ship a member a
        // consumer would hover over and see nothing for.
        string[] undocumented = [.. Assemblies().SelectMany(Undocumented)];

        Assert.True(
            undocumented.Length == 0,
            $"Public members with no XML documentation: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void TheDocumentationCheck_ActuallyFindsDocumentation()
    {
        // Guards the test above: if the XML file stopped being found or the keys stopped matching,
        // "nothing undocumented" would be true by finding nothing at all.
        var documented = DocumentedNames(typeof(BinarySerializer).Assembly);

        Assert.True(documented.Count > 100, $"Only {documented.Count} documented members were found.");
        Assert.Contains("ViShap.Viper.BinarySerializer", documented);
        Assert.Contains("ViShap.Viper.BinarySerializer.Serialize", documented);
        Assert.Contains("ViShap.Viper.Metadata.BinaryFormatInspector.Peek", documented);
    }

    private static Assembly[] Assemblies() =>
        [.. new[] { typeof(BinarySerializer).Assembly, typeof(BinarySerializerException).Assembly }.Distinct()];

    /// <summary>
    /// What the assembly's generated XML file documents, as "Namespace.Type" and
    /// "Namespace.Type.Member". Parameter lists and generic arity are dropped: an overload set is
    /// documented member by member in the file, and the question here is whether a name is covered at
    /// all, not how the compiler spells one signature.
    /// </summary>
    private static HashSet<string> DocumentedNames(Assembly assembly)
    {
        string path = Path.ChangeExtension(assembly.Location, ".xml");
        Assert.True(File.Exists(path), $"No XML documentation file was produced for '{assembly.GetName().Name}'.");

        return
        [
            .. XDocument.Load(path)
                .Descendants("member")
                .Select(member => member.Attribute("name")?.Value)
                .Where(name => name is not null)
                .Select(name => Simplify(name!))
        ];
    }

    /// <summary>Drops the key's kind prefix, its parameter list and any generic arity.</summary>
    private static string Simplify(string key)
    {
        string name = key[2..];

        int parameters = name.IndexOf('(', StringComparison.Ordinal);
        if (parameters >= 0)
            name = name[..parameters];

        int arity = name.IndexOf('`', StringComparison.Ordinal);
        return arity >= 0 ? name[..arity] : name;
    }

    /// <summary>Every exported type and member of <paramref name="assembly"/> the XML file omits.</summary>
    private static IEnumerable<string> Undocumented(Assembly assembly)
    {
        var documented = DocumentedNames(assembly);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!documented.Contains(type.FullName!))
                yield return type.FullName!;

            var members = type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

            foreach (var member in members)
            {
                if (IsCompilerSupplied(member))
                    continue;

                string name = member is ConstructorInfo ? "#ctor" : member.Name;
                if (!documented.Contains($"{type.FullName}.{name}"))
                    yield return $"{type.FullName}.{name}";
            }
        }
    }

    /// <summary>
    /// Members no author writes: an enum's backing field and its values, the equality, cloning and
    /// printing members the compiler adds to a record, and the default constructor it supplies to a
    /// type that declares none. The documentation file carries none of them and CS1591 does not ask
    /// for them, so demanding documentation here would demand what the compiler cannot produce.
    /// </summary>
    private static bool IsCompilerSupplied(MemberInfo member) =>
        member.DeclaringType!.IsEnum ||
        member is MethodInfo { IsSpecialName: true } ||
        member is ConstructorInfo { } constructor && constructor.GetParameters().Length == 0 ||
        member.Name is "Equals" or "GetHashCode" or "ToString" or "PrintMembers" or "Deconstruct" or "<Clone>$";
}
