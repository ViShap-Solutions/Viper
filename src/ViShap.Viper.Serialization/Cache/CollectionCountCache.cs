using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace ViShap.Viper.Cache;

/// <summary>
/// Reads the element count of a container through whichever counting interface it implements.
/// <para>
/// The non-generic <c>ICollection</c> is not enough: a set, a frozen collection and most immutable
/// containers implement only the generic ones, and their count is exactly what says whether a payload
/// declared more elements than the container ended up holding.
/// </para>
/// </summary>
internal static class CollectionCountCache
{
    private static readonly ConcurrentDictionary<Type, Func<object, int>?> Accessors = new();

    /// <summary>The count accessor for <paramref name="type"/>, or <c>null</c> when it has no count.</summary>
    public static Func<object, int>? GetCount(Type type) => Accessors.GetOrAdd(type, Build);

    /// <summary>The count of <paramref name="value"/>, or <c>null</c> when its type has none.</summary>
    public static int? CountOf(object value) =>
        GetCount(value.GetType()) is { } accessor ? accessor(value) : null;

    private static Func<object, int>? Build(Type type)
    {
        var source = CountingInterface(type);
        if (source is null)
            return null;

        var count = source.GetProperty("Count");
        if (count is null)
            return null;

        var instance = Expression.Parameter(typeof(object), "instance");
        return Expression
            .Lambda<Func<object, int>>(
                Expression.Property(Expression.Convert(instance, source), count), instance)
            .Compile();
    }

    private static Type? CountingInterface(Type type)
    {
        if (typeof(System.Collections.ICollection).IsAssignableFrom(type))
            return typeof(System.Collections.ICollection);

        var interfaces = type.GetInterfaces();

        return Closed(interfaces, typeof(ICollection<>))
               ?? Closed(interfaces, typeof(IReadOnlyCollection<>));
    }

    private static Type? Closed(Type[] interfaces, Type definition) =>
        interfaces.FirstOrDefault(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition);
}
