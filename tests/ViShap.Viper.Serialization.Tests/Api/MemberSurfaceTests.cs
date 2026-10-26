using System.Reflection;
using System.Runtime.CompilerServices;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins API-29: every public member of every public type — and every protected member of a public type
/// a consumer can derive from — is listed in contract §3.6, and every member the list names exists. The
/// list is read from <c>internal/System-Contract.md</c> itself, so the contract cannot drift from the
/// assemblies in either direction.
/// </summary>
public class MemberSurfaceTests
{
    private const string Heading = "## 3.6 Member surface";

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void EveryPublicMember_IsListedInTheContract()
    {
        string[] unlisted = [.. Actual().Except(Listed(), StringComparer.Ordinal)];

        Assert.True(unlisted.Length == 0, $"Public members absent from contract §3.6:\n{string.Join("\n", unlisted)}");
    }

    [Fact]
    public void EveryListedMember_Exists()
    {
        string[] missing = [.. Listed().Except(Actual(), StringComparer.Ordinal)];

        Assert.True(missing.Length == 0, $"Members contract §3.6 lists that do not exist:\n{string.Join("\n", missing)}");
    }

    [Fact]
    public void TheList_IsFoundAndNotEmpty()
    {
        Assert.Contains("ViShap.Viper.BinarySerializer :: .ctor(BinarySerializerOptions)", Listed());
        Assert.True(Listed().Length > 200, $"Only {Listed().Length} members were read from contract §3.6.");
    }

    /// <summary>The lines of the first <c>text</c> block under the §3.6 heading.</summary>
    private static string[] Listed()
    {
        string contract = File.ReadAllText(Path.Combine(SourceTree.RepositoryRoot, "internal", "System-Contract.md"));

        int heading = contract.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(heading >= 0, $"Contract has no '{Heading}' section.");

        int start = contract.IndexOf("```text", heading, StringComparison.Ordinal) + "```text".Length;
        int end = contract.IndexOf("```", start, StringComparison.Ordinal);

        return
        [
            .. contract[start..end]
                .Split('\n')
                .Select(line => line.TrimEnd('\r').Trim())
                .Where(line => line.Length > 0)
        ];
    }

    private static string[] Actual() =>
        [.. new[] { typeof(BinarySerializer).Assembly, typeof(BinarySerializerException).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsNested)
            .SelectMany(type => Members(type).Select(member => $"{type.FullName} :: {member}"))
            .Distinct()
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                yield return $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue())}";

            yield break;
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (Visible(type, constructor))
                yield return $"{Access(constructor)}.ctor({Parameters(constructor)})";
        }

        foreach (var property in type.GetProperties(Declared))
        {
            if (!Visible(type, property.GetMethod) && !Visible(type, property.SetMethod))
                continue;

            var getter = Visible(type, property.GetMethod) ? " get;" : string.Empty;
            var setter = property.SetMethod is { } set && Visible(type, set)
                ? set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) ? " init;" : " set;"
                : string.Empty;
            string owner = (property.GetMethod ?? property.SetMethod)!.IsStatic ? "static " : string.Empty;
            string index = property.GetIndexParameters() is { Length: > 0 } indices
                ? $"[{string.Join(", ", indices.Select(parameter => Spell(parameter.ParameterType)))}]"
                : string.Empty;

            yield return $"{Access((property.GetMethod ?? property.SetMethod)!)}{owner}{property.Name}{index} : {Spell(property.PropertyType)} {{{getter}{setter} }}";
        }

        foreach (var field in type.GetFields(Declared))
        {
            if (field.IsPublic || (!type.IsSealed && (field.IsFamily || field.IsFamilyOrAssembly)))
                yield return $"{(field.IsStatic ? "static " : string.Empty)}{field.Name} : {Spell(field.FieldType)}";
        }

        foreach (var method in type.GetMethods(Declared))
        {
            if (!Visible(type, method))
                continue;

            if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal))
                continue;

            if (method.Name == "<Clone>$")
                continue;

            string generics = method.IsGenericMethodDefinition
                ? $"<{string.Join(", ", method.GetGenericArguments().Select(Spell))}>"
                : string.Empty;

            yield return $"{Access(method)}{(method.IsStatic ? "static " : string.Empty)}{method.Name}{generics}({Parameters(method)}) : {Spell(method.ReturnType)}";
        }
    }

    /// <summary>
    /// Whether a consumer reaches <paramref name="method"/>: it is public, or it is protected on a type
    /// the consumer can derive from.
    /// </summary>
    private static bool Visible(Type type, MethodBase? method) =>
        method is not null &&
        (method.IsPublic || (!type.IsSealed && (method.IsFamily || method.IsFamilyOrAssembly)));

    private static string Access(MethodBase method) => method.IsPublic ? string.Empty : "protected ";

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
            parameter.IsOut ? $"out {Spell(parameter.ParameterType.GetElementType()!)}"
            : parameter.ParameterType.IsByRef && parameter.IsIn ? $"in {Spell(parameter.ParameterType.GetElementType()!)}"
            : parameter.ParameterType.IsByRef ? $"ref {Spell(parameter.ParameterType.GetElementType()!)}"
            : Spell(parameter.ParameterType)));

    private static string Spell(Type type)
    {
        if (type.IsGenericParameter)
            return type.Name;

        if (type.IsArray)
            return $"{Spell(type.GetElementType()!)}[]";

        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return $"{Spell(underlying)}?";

        if (!type.IsGenericType)
            return type.Name;

        string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Spell))}>";
    }
}
