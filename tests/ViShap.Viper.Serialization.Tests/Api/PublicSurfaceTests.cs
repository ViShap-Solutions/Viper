using System.Reflection;
using System.Xml.Linq;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins EXT-01, EXT-04 and EXT-05: the compiled public surface is exactly the one contract §3 lists —
/// its types, and the members of the serializer and the builder — and every member of it is
/// documented. A type that appears here without appearing there is an
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
        "ViShap.Viper.PooledPayload",
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
            "ViShap.Viper.Engine.Graph",
            "ViShap.Viper.Engine.Codec`1",
            "ViShap.Viper.Engine.FormatterCache`1",
            "ViShap.Viper.Engine.TypeContract",
            "ViShap.Viper.Engine.TypeContract`1",
            "ViShap.Viper.Engine.ReflectedContract`1",
            "ViShap.Viper.Engine.MemberWriter",
            "ViShap.Viper.Engine.MemberReader",
            "ViShap.Viper.Engine.GraphState",
            "ViShap.Viper.Io.WireReader",
            "ViShap.Viper.Io.WireWriter",
            "ViShap.Viper.Io.PayloadBuffer",
            "ViShap.Viper.Io.ElementCount",
            "ViShap.Viper.Security.SerializationBudget",
            "ViShap.Viper.Security.OperationState",
            "ViShap.Viper.Pipeline.FrameReader",
            "ViShap.Viper.Formatters.IScalarFormatter`1",
            "ViShap.Viper.Formatters.ISequenceShape`4",
            "ViShap.Viper.Formatters.IMapShape`5",
            "ViShap.Viper.Formatters.ICompositeFormatter`1",
            "ViShap.Viper.Formatters.FormatterRegistry",
            "ViShap.Viper.Pipeline.FormatRouter"
        ];

        // Every name must be a type that exists, so the check cannot pass by naming nothing.
        Assert.All(internalNames, name => Assert.NotNull(typeof(BinarySerializer).Assembly.GetType(name)));

        var exported = ActualSurface();

        Assert.All(internalNames, name => Assert.DoesNotContain(name, exported));
    }

    // --- EXT-05: the serializer and the builder expose exactly the members §3.1 and §4.1 list -----

    /// <summary>The public members of <see cref="BinarySerializer"/> that contract §3.1 lists.</summary>
    private static readonly string[] SerializerSurface =
    [
        ".ctor(BinarySerializerOptions)",
        "Serialize<T>(IBufferWriter<Byte>, T) : Void",
        "Serialize<T>(T) : Byte[]",
        "SerializePooled<T>(T) : PooledPayload",
        "Serialize<T>(Stream, T) : Void",
        "SerializeAsync<T>(Stream, T, CancellationToken) : ValueTask",
        "SerializeAsync<T>(PipeWriter, T, CancellationToken) : ValueTask",
        "Deserialize<T>(ReadOnlySpan<Byte>) : T",
        "Deserialize<T>(ReadOnlySpan<Byte>, out Int32) : T",
        "Deserialize<T>(ReadOnlySequence<Byte>) : T",
        "Deserialize<T>(ReadOnlySequence<Byte>, out SequencePosition) : T",
        "Deserialize<T>(Stream) : T",
        "DeserializeAsync<T>(Stream, CancellationToken) : ValueTask<T>",
        "DeserializeAsync<T>(PipeReader, CancellationToken) : ValueTask<T>",
        "DeserializeAsyncEnumerable<T>(Stream, CancellationToken) : IAsyncEnumerable<T>",
        "DeserializeAsyncEnumerable<T>(PipeReader, CancellationToken) : IAsyncEnumerable<T>",
        "Populate<T>(ReadOnlySpan<Byte>, T) : Void",
        "Populate<T>(ReadOnlySpan<Byte>, T, out Int32) : Void",
        "Populate<T>(ReadOnlySequence<Byte>, T) : Void",
        "Populate<T>(ReadOnlySequence<Byte>, T, out SequencePosition) : Void",
        "Populate<T>(Stream, T) : Void",
        "PopulateAsync<T>(Stream, T, CancellationToken) : ValueTask",
        "PopulateAsync<T>(PipeReader, T, CancellationToken) : ValueTask"
    ];

    /// <summary>The public members of <see cref="BinarySerializerOptionsBuilder"/> that contract §4.1 lists.</summary>
    private static readonly string[] BuilderSurface =
    [
        "WithCompression(ICompressionAlgorithm) : BinarySerializerOptionsBuilder",
        "WithChecksum(IChecksumAlgorithm) : BinarySerializerOptionsBuilder",
        "WithEncryption(IEncryptionAlgorithm, ReadOnlySpan<Byte>, String) : BinarySerializerOptionsBuilder",
        "WithEncryption(IEncryptionAlgorithm, Func<String, Byte[]>, String) : BinarySerializerOptionsBuilder",
        "WithEncryption(IEncryptionAlgorithm, IKeyProvider, String) : BinarySerializerOptionsBuilder",
        "WithKeys(ReadOnlySpan<Byte>, String) : BinarySerializerOptionsBuilder",
        "WithKeys(Func<String, Byte[]>) : BinarySerializerOptionsBuilder",
        "WithKeys(IKeyProvider) : BinarySerializerOptionsBuilder",
        "WithVersion(Int32) : BinarySerializerOptionsBuilder",
        "PreserveReferences(Boolean) : BinarySerializerOptionsBuilder",
        "WithLimits(SerializationLimits) : BinarySerializerOptionsBuilder",
        "AllowV0Fallback(Boolean) : BinarySerializerOptionsBuilder",
        "RequireEncryption(Boolean) : BinarySerializerOptionsBuilder",
        "RequireChecksum(Boolean) : BinarySerializerOptionsBuilder",
        "RegisterCustomCompression(String, Func<ICompressionAlgorithm>) : BinarySerializerOptionsBuilder",
        "RegisterCustomChecksum(String, Func<IChecksumAlgorithm>) : BinarySerializerOptionsBuilder",
        "RegisterCustomEncryption(String, Func<IEncryptionAlgorithm>) : BinarySerializerOptionsBuilder",
        "Build() : BinarySerializerOptions"
    ];

    [Fact]
    public void BinarySerializer_ExposesExactlyTheContractMembers()
    {
        Assert.Equal(
            SerializerSurface.Order(StringComparer.Ordinal),
            PublicMembers(typeof(BinarySerializer)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BinarySerializerOptionsBuilder_ExposesExactlyTheContractMembers()
    {
        Assert.Equal(
            BuilderSurface.Order(StringComparer.Ordinal),
            PublicMembers(typeof(BinarySerializerOptionsBuilder)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BinarySerializerOptions_ExposesNoFactoryBesidesConfigure()
    {
        string[] factories =
        [
            .. typeof(BinarySerializerOptions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
        ];

        Assert.Equal(["Configure"], factories);
    }

    /// <summary>
    /// The declared public constructors and methods of <paramref name="type"/>, spelled as
    /// <c>Name&lt;T&gt;(Parameter, …) : Return</c> with simple type names and without nullability,
    /// which reflection does not carry on a type.
    /// </summary>
    private static IEnumerable<string> PublicMembers(Type type)
    {
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            yield return $".ctor({Parameters(constructor)})";

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName || IsCompilerSupplied(method))
                continue;

            string generics = method.IsGenericMethodDefinition
                ? $"<{string.Join(", ", method.GetGenericArguments().Select(Spell))}>"
                : string.Empty;

            yield return $"{method.Name}{generics}({Parameters(method)}) : {Spell(method.ReturnType)}";
        }
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
            parameter.IsOut ? $"out {Spell(parameter.ParameterType.GetElementType()!)}"
            : parameter.ParameterType.IsByRef ? $"ref {Spell(parameter.ParameterType.GetElementType()!)}"
            : Spell(parameter.ParameterType)));

    private static string Spell(Type type)
    {
        if (type.IsGenericParameter)
            return type.Name;

        if (type.IsArray)
            return $"{Spell(type.GetElementType()!)}[]";

        if (!type.IsGenericType)
            return type.Name;

        string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Spell))}>";
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
