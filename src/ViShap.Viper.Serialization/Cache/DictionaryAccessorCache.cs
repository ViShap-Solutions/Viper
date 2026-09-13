using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace ViShap.Viper.Cache;

internal static class DictionaryAccessorCache
{
    private static readonly ConcurrentDictionary<Type, EntryAccessors> EntryAccessorByType = new();

    public static EntryAccessors GetEntryAccessors(Type entryType) =>
        EntryAccessorByType.GetOrAdd(entryType, BuildEntryAccessors);
    
    private static EntryAccessors BuildEntryAccessors(Type entryType)
    {
        var keyProp = entryType.GetProperty("Key")
            ?? throw new BinaryTypeException($"Dictionary entry type '{entryType}' does not expose Key.");

        var valueProp = entryType.GetProperty("Value")
            ?? throw new BinaryTypeException($"Dictionary entry type '{entryType}' does not expose Value.");

        var entryParam = Expression.Parameter(typeof(object), "entry");
        var typedEntry = Expression.Convert(entryParam, entryType);

        var keyExpr = Expression.Convert(Expression.Property(typedEntry, keyProp), typeof(object));
        var valueExpr = Expression.Convert(Expression.Property(typedEntry, valueProp), typeof(object));

        var keyGetter = Expression.Lambda<Func<object, object?>>(keyExpr, entryParam).Compile();
        var valueGetter = Expression.Lambda<Func<object, object?>>(valueExpr, entryParam).Compile();

        return new EntryAccessors(keyGetter, valueGetter);
    }

    internal sealed record EntryAccessors(
        Func<object, object?> KeyGetter,
        Func<object, object?> ValueGetter);
}