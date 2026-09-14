using System.Collections.Concurrent;

namespace ViShap.Viper.Cache;

internal static class PolymorphicTypeCache
{
    private const int MaxKnownTypes = 255;

    private static readonly ConcurrentDictionary<Type, PolymorphicMap?> Cache = new();

    public static PolymorphicMap? GetMap(Type declaredType) => Cache.GetOrAdd(declaredType, BuildMap);

    private static PolymorphicMap? BuildMap(Type declaredType)
    {
        var attrs = declaredType.GetCustomAttributes(typeof(BinaryUnionAttribute), inherit: false)
            .Cast<BinaryUnionAttribute>()
            .ToArray();

        if (attrs.Length == 0) return null;

        var duplicateTags = attrs.GroupBy(a => a.Tag).Where(g => g.Count() > 1).ToArray();
        if (duplicateTags.Length > 0)
            throw new BinaryTypeException($"'{declaredType}' has duplicate [BinaryUnion] tag(s): {string.Join(", ", duplicateTags.Select(g => g.Key))}.");

        foreach (var a in attrs)
        {
            if (a.Tag is < 0 or > MaxKnownTypes)
                throw new BinaryTypeException($"[BinaryUnion] tag {a.Tag} on '{declaredType}' must fit in a byte (0-{MaxKnownTypes}).");
            if (!declaredType.IsAssignableFrom(a.DerivedType))
                throw new BinaryTypeException($"Known type '{a.DerivedType}' is not assignable to '{declaredType}'.");
        }

        var byType = attrs.ToDictionary(a => a.DerivedType, a => (byte)a.Tag);
        var byId = attrs.ToDictionary(a => (byte)a.Tag, a => a.DerivedType);

        return new PolymorphicMap(byType, byId);
    }
}

internal sealed class PolymorphicMap(
    IReadOnlyDictionary<Type, byte> discriminatorByType,
    IReadOnlyDictionary<byte, Type> typeByDiscriminator)
{
    public bool TryGetDiscriminator(Type runtimeType, out byte discriminator) =>
        discriminatorByType.TryGetValue(runtimeType, out discriminator);

    public bool TryGetType(byte discriminator, out Type? runtimeType)
    {
        var ok = typeByDiscriminator.TryGetValue(discriminator, out var t);
        runtimeType = t;
        return ok;
    }
}