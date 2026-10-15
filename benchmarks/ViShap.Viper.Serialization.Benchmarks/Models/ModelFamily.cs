namespace ViShap.Viper.Serialization.Benchmarks.Models;

/// <summary>
/// A set of model types written for one library (Benchmark-Plan §2). Every family carries the same
/// types under the same names as the Viper models, which are the logical schema of the corpus; only
/// the attributes a library needs differ, and FAIR-19 proves that nothing else does.
/// </summary>
internal enum ModelFamily
{
    Viper,
    MemoryPack,
    MessagePack,
    ProtobufNet,
    NerdbankMessagePack,
    Orleans,
    SystemTextJson,
}

/// <summary>A structural difference a library's model must carry, recorded per member (FAIR-20).</summary>
internal sealed record ModelDifference(ModelFamily Family, string Type, string Member, string Difference, string Reason);

internal static class ModelFamilies
{
    private const string Root = "ViShap.Viper.Serialization.Benchmarks.Models";

    internal static IReadOnlyList<ModelFamily> All { get; } = Enum.GetValues<ModelFamily>();

    internal static IReadOnlyList<ModelFamily> Competitors { get; } = [.. All.Where(family => family != ModelFamily.Viper)];

    /// <summary>The namespace a family's types live in.</summary>
    internal static string Namespace(ModelFamily family) => family switch
    {
        ModelFamily.Viper => $"{Root}.Viper",
        ModelFamily.MemoryPack => $"{Root}.MemoryPackVariant",
        ModelFamily.MessagePack => $"{Root}.MessagePackVariant",
        ModelFamily.ProtobufNet => $"{Root}.ProtobufNetVariant",
        ModelFamily.NerdbankMessagePack => $"{Root}.NerdbankVariant",
        ModelFamily.Orleans => $"{Root}.OrleansVariant",
        ModelFamily.SystemTextJson => $"{Root}.SystemTextJsonVariant",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>Whether a type is a model of some family rather than a library or framework type.</summary>
    internal static bool IsModel(Type type) =>
        type.Namespace is { } ns && ns.StartsWith(Root + ".", StringComparison.Ordinal) && type.Assembly == typeof(ModelFamilies).Assembly;

    /// <summary>The type of <paramref name="family"/> with the given simple name.</summary>
    internal static Type Resolve(ModelFamily family, string name) =>
        typeof(ModelFamilies).Assembly.GetType($"{Namespace(family)}.{name}")
        ?? throw new InvalidOperationException($"The {family} models have no type '{name}'.");

    /// <summary>
    /// The type <paramref name="type"/> becomes in <paramref name="family"/>: a model is replaced by its
    /// namesake, and a generic or an array is rebuilt over the replaced arguments. Everything else is
    /// shared, because every family uses the same framework types.
    /// </summary>
    internal static Type Map(Type type, ModelFamily family)
    {
        if (IsModel(type))
            return Resolve(family, type.Name);

        if (type.IsArray)
        {
            var element = Map(type.GetElementType()!, family);
            return type.GetArrayRank() == 1 && type.IsSZArray ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
        }

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments().Select(argument => Map(argument, family)).ToArray();
            return type.GetGenericTypeDefinition().MakeGenericType(arguments);
        }

        return type;
    }

    /// <summary>Whether a type mentions a model anywhere — itself, an element, a generic argument.</summary>
    internal static bool MentionsModel(Type type) =>
        IsModel(type)
        || (type.IsArray && MentionsModel(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(MentionsModel));

    /// <summary>
    /// The structural differences of FAIR-20: what a family's model carries that the schema does not,
    /// because the library cannot express the schema's shape otherwise.
    /// </summary>
    internal static IReadOnlyList<ModelDifference> Differences { get; } =
    [
        new(ModelFamily.MemoryPack, "EventBase", "(type)", "abstract",
            "MemoryPackUnion is declared on an abstract class or an interface; the corpus holds no bare EventBase, so no value changes"),
        new(ModelFamily.MessagePack, "EventBase", "(type)", "abstract",
            "on a concrete base [Union] wrote and read the base, and every arm came back as EventBase (observed in verification); the corpus holds no bare EventBase, so no value changes"),
    ];
}
