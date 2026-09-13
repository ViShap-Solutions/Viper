using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ViShap.Viper.Cache;

internal static class TypeAccessorCache
{
    private static readonly ConcurrentDictionary<Type, TypeAccessorPlan> Cache = new();

    public static TypeAccessorPlan GetOrBuild(Type type) => Cache.GetOrAdd(type, BuildPlan);

    private static TypeAccessorPlan BuildPlan(Type type)
    {
        var propertyMembers = type
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => !typeof(Delegate).IsAssignableFrom(p.PropertyType))
            .Where(p => p.GetCustomAttribute<BinaryIgnoreAttribute>() is null)
            .Where(IsIncluded)
            .Select(p => new MemberSource(
                p.Name,
                p.GetCustomAttribute<BinaryOrderAttribute>()?.Order ?? int.MaxValue,
                BuildAccessor(p)));

        var fieldMembers = type
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly)
            .Where(f => !typeof(Delegate).IsAssignableFrom(f.FieldType))
            .Where(f => f.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(f => f.GetCustomAttribute<BinaryIgnoreAttribute>() is null)
            .Where(IsIncluded)
            .Select(f => new MemberSource(
                f.Name,
                f.GetCustomAttribute<BinaryOrderAttribute>()?.Order ?? int.MaxValue,
                BuildAccessor(f)));

        var members = propertyMembers
            .Concat(fieldMembers)
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => m.Accessor)
            .ToArray();

        return new TypeAccessorPlan { Type = type, Members = members };
    }

    private static bool IsIncluded(PropertyInfo p) =>
        p.GetMethod is { IsPublic: true } || p.GetCustomAttribute<BinaryIncludeAttribute>() is not null;

    private static bool IsIncluded(FieldInfo f) =>
        f.IsPublic || f.GetCustomAttribute<BinaryIncludeAttribute>() is not null;

    private static MemberAccessor BuildAccessor(PropertyInfo property)
    {
        return new MemberAccessor
        {
            Name = property.Name,
            MemberType = property.PropertyType,
            Getter = BuildPropertyGetter(property),
            Setter = BuildPropertySetter(property)
        };
    }

    private static MemberAccessor BuildAccessor(FieldInfo field)
    {
        return new MemberAccessor
        {
            Name = field.Name,
            MemberType = field.FieldType,
            Getter = BuildFieldGetter(field),
            Setter = BuildFieldSetter(field)
        };
    }

    private static Func<object, object?> BuildPropertyGetter(PropertyInfo property)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var typedInstance = property.DeclaringType!.IsValueType
                                        ? Expression.Unbox(instanceParam, property.DeclaringType!)
                                        : Expression.Convert(instanceParam, property.DeclaringType!);
        var propertyAccess = Expression.Property(typedInstance, property);
        var boxedResult = Expression.Convert(propertyAccess, typeof(object));

        return Expression.Lambda<Func<object, object?>>(boxedResult, instanceParam).Compile();
    }

    private static Action<object, object?> BuildPropertySetter(PropertyInfo property)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var typedInstance = property.DeclaringType!.IsValueType
                                        ? Expression.Unbox(instanceParam, property.DeclaringType!)
                                        : Expression.Convert(instanceParam, property.DeclaringType!);
        var typedValue = Expression.Convert(valueParam, property.PropertyType);
        var propertyAccess = Expression.Property(typedInstance, property);
        var assign = Expression.Assign(propertyAccess, typedValue);

        return Expression.Lambda<Action<object, object?>>(assign, instanceParam, valueParam).Compile();
    }

    private static Func<object, object?> BuildFieldGetter(FieldInfo field)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var typedInstance = field.DeclaringType!.IsValueType
                                        ? Expression.Unbox(instanceParam, field.DeclaringType!)
                                        : Expression.Convert(instanceParam, field.DeclaringType!);
        var fieldAccess = Expression.Field(typedInstance, field);
        var boxedResult = Expression.Convert(fieldAccess, typeof(object));

        return Expression.Lambda<Func<object, object?>>(boxedResult, instanceParam).Compile();
    }

    private static Action<object, object?> BuildFieldSetter(FieldInfo field)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var valueParam = Expression.Parameter(typeof(object), "value");
        var typedInstance = field.DeclaringType!.IsValueType
                                        ? Expression.Unbox(instanceParam, field.DeclaringType!)
                                        : Expression.Convert(instanceParam, field.DeclaringType!);
        var typedValue = Expression.Convert(valueParam, field.FieldType);
        var fieldAccess = Expression.Field(typedInstance, field);
        var assign = Expression.Assign(fieldAccess, typedValue);

        return Expression.Lambda<Action<object, object?>>(assign, instanceParam, valueParam).Compile();
    }

    public static int CachedTypeCount => Cache.Count;

    private sealed record MemberSource(string Name, int Order, MemberAccessor Accessor);
}