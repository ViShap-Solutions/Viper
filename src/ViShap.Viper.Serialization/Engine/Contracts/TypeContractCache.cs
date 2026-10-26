using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ViShap.Viper.Engine;

/// <summary>Tag ↔ type map declared by <see cref="BinaryUnionAttribute"/> on a base type.</summary>
internal sealed class UnionMap(
    IReadOnlyDictionary<Type, byte> tagByType,
    IReadOnlyDictionary<byte, Type> typeByTag)
{
    public int Count => tagByType.Count;

    public bool TryGetTag(Type runtimeType, out byte tag) => tagByType.TryGetValue(runtimeType, out tag);

    /// <summary>The arms in ascending tag order, each as <c>(tag, type)</c>.</summary>
    public string Describe() =>
        string.Join(", ", typeByTag.OrderBy(arm => arm.Key).Select(arm => $"({arm.Key}, {arm.Value})"));

    public bool TryGetType(byte tag, out Type? runtimeType)
    {
        bool found = typeByTag.TryGetValue(tag, out var type);
        runtimeType = type;
        return found;
    }
}

/// <summary>
/// The reflected contract of every member-encoded type and the union map of every declared type, each
/// built once, on first use, and shared by every serializer in the process. Building either can fail with
/// <see cref="BinaryTypeException"/>; a failure is not kept, so every later use reports it again.
/// Nothing an operation owns takes part in building an entry.
/// </summary>
internal static class TypeContractCache
{
    private const int MaxUnionTag = 255;

    private static readonly ConcurrentDictionary<Type, ITypeContract> Contracts = new();
    private static readonly ConcurrentDictionary<Type, UnionMap?> Unions = new();

    /// <summary>
    /// The contract of <paramref name="type"/> built by reflection, which the polymorphic slot finds by
    /// runtime type. The engine reaches it only while the reflection path is enabled.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static ITypeContract Get(Type type) => Contracts.GetOrAdd(type, ReflectedContract.Build);

    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static TypeContract<T> Get<T>() => (TypeContract<T>)Get(typeof(T));

    public static UnionMap? GetUnion(Type declaredType) => Unions.GetOrAdd(declaredType, BuildUnion);

    public static int CachedTypeCount => Contracts.Count;

    /// <summary>
    /// Why the <see cref="BinaryUnionAttribute"/> arms of <paramref name="declaredType"/> cannot form a
    /// union map, or <see langword="null"/> when they can or there are none.
    /// </summary>
    public static string? UnionError(Type declaredType)
    {
        var attributes = UnionAttributes(declaredType);

        var duplicateTags = attributes.GroupBy(a => a.Tag).Where(g => g.Count() > 1).ToArray();
        if (duplicateTags.Length > 0)
            return $"'{declaredType}' has duplicate [BinaryUnion] tag(s): " +
                   $"{string.Join(", ", duplicateTags.Select(g => g.Key))}.";

        foreach (var attribute in attributes)
        {
            if (attribute.Tag is < 0 or > MaxUnionTag)
                return $"[BinaryUnion] tag {attribute.Tag} on '{declaredType}' must fit in a byte " +
                       $"(0-{MaxUnionTag}).";

            if (!declaredType.IsAssignableFrom(attribute.DerivedType))
                return $"Known type '{attribute.DerivedType}' is not assignable to '{declaredType}'.";
        }

        return null;
    }

    private static UnionMap? BuildUnion(Type declaredType)
    {
        if (UnionError(declaredType) is { } error)
            throw new BinaryTypeException(error);

        var attributes = UnionAttributes(declaredType);
        if (attributes.Length == 0)
            return null;

        return new UnionMap(
            attributes.ToDictionary(a => a.DerivedType, a => (byte)a.Tag),
            attributes.ToDictionary(a => (byte)a.Tag, a => a.DerivedType));
    }

    private static BinaryUnionAttribute[] UnionAttributes(Type declaredType) =>
        declaredType
            .GetCustomAttributes(typeof(BinaryUnionAttribute), inherit: false)
            .Cast<BinaryUnionAttribute>()
            .ToArray();
}
