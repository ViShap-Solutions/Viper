using System.Collections.Concurrent;

namespace ViShap.Viper.Cache;

internal static class PolymorphicTypeCache
{
    private const int MaxKnownTypes = 256;

    private static readonly ConcurrentDictionary<Type, PolymorphicMap?> Cache = new();

    public static PolymorphicMap? GetMap(Type declaredType) => Cache.GetOrAdd(declaredType, BuildMap);

    private static PolymorphicMap? BuildMap(Type declaredType)
    {
        var attrs = declaredType.GetCustomAttributes(typeof(BinaryKnownTypeAttribute), inherit: false)
            .Cast<BinaryKnownTypeAttribute>()
            .ToArray();

        if (attrs.Length == 0) return null;

        var knownTypes = attrs.Select(a => a.DerivedType).Distinct().ToArray();

        if (knownTypes.Length > MaxKnownTypes)
            throw new BinaryTypeException($"'{declaredType}' has {knownTypes.Length} [BinaryKnownType] entries — the byte discriminator supports at most {MaxKnownTypes}.");

        foreach (var t in knownTypes)
        {
            if (!declaredType.IsAssignableFrom(t))
                throw new BinaryTypeException($"Known type '{t}' is not assignable to '{declaredType}'.");
        }
        
        var byType = new Dictionary<Type, byte>();
        var byId = new Dictionary<byte, Type>();

        for (byte id = 0; id < knownTypes.Length; id++)
        {
            byType[knownTypes[id]] = id;
            byId[id] = knownTypes[id];
        }

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