using System.Collections.Concurrent;
using System.Reflection;

namespace ViShap.Viper.Engine;

/// <summary>Tag ↔ type map declared by <see cref="BinaryUnionAttribute"/> on a base type.</summary>
internal sealed class UnionMap(
    IReadOnlyDictionary<Type, byte> tagByType,
    IReadOnlyDictionary<byte, Type> typeByTag)
{
    public bool TryGetTag(Type runtimeType, out byte tag) => tagByType.TryGetValue(runtimeType, out tag);

    public bool TryGetType(byte tag, out Type? runtimeType)
    {
        bool found = typeByTag.TryGetValue(tag, out var type);
        runtimeType = type;
        return found;
    }
}

/// <summary>
/// The contract of every member-encoded type and the union map of every declared type, each built
/// once, on first use, and shared by every serializer in the process. Building either can fail with
/// <see cref="BinaryTypeException"/>; a failure is not kept, so every later use reports it again.
/// Nothing an operation owns takes part in building an entry.
/// </summary>
internal static class TypeContractCache
{
    private const int MaxUnionTag = 255;

    private static readonly ConcurrentDictionary<Type, TypeContract> Contracts = new();
    private static readonly ConcurrentDictionary<Type, UnionMap?> Unions = new();

    /// <summary>The contract of <paramref name="type"/>, which the polymorphic slot finds by runtime type.</summary>
    public static TypeContract Get(Type type) => Contracts.GetOrAdd(type, ReflectedContract.Build);

    public static TypeContract<T> Get<T>() => (TypeContract<T>)Get(typeof(T));

    public static UnionMap? GetUnion(Type declaredType) => Unions.GetOrAdd(declaredType, BuildUnion);

    public static int CachedTypeCount => Contracts.Count;

    private static UnionMap? BuildUnion(Type declaredType)
    {
        var attributes = declaredType
            .GetCustomAttributes(typeof(BinaryUnionAttribute), inherit: false)
            .Cast<BinaryUnionAttribute>()
            .ToArray();

        if (attributes.Length == 0)
            return null;

        var duplicateTags = attributes.GroupBy(a => a.Tag).Where(g => g.Count() > 1).ToArray();
        if (duplicateTags.Length > 0)
            throw new BinaryTypeException(
                $"'{declaredType}' has duplicate [BinaryUnion] tag(s): " +
                $"{string.Join(", ", duplicateTags.Select(g => g.Key))}.");

        foreach (var attribute in attributes)
        {
            if (attribute.Tag is < 0 or > MaxUnionTag)
                throw new BinaryTypeException(
                    $"[BinaryUnion] tag {attribute.Tag} on '{declaredType}' must fit in a byte " +
                    $"(0-{MaxUnionTag}).");

            if (!declaredType.IsAssignableFrom(attribute.DerivedType))
                throw new BinaryTypeException(
                    $"Known type '{attribute.DerivedType}' is not assignable to '{declaredType}'.");
        }

        return new UnionMap(
            attributes.ToDictionary(a => a.DerivedType, a => (byte)a.Tag),
            attributes.ToDictionary(a => (byte)a.Tag, a => a.DerivedType));
    }
}
