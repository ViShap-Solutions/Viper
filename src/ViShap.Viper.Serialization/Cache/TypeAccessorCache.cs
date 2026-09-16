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
        bool isContract = type.GetCustomAttribute<BinaryContractAttribute>() is not null;

        var propertyCandidates = type
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => !typeof(Delegate).IsAssignableFrom(p.PropertyType))
            .Select(p => new MemberCandidate(
                p.Name,
                IsPubliclyVisible: p.GetMethod!.IsPublic,
                HasIgnore: p.GetCustomAttribute<BinaryIgnoreAttribute>() is not null,
                HasInclude: p.GetCustomAttribute<BinaryIncludeAttribute>() is not null,
                Order: p.GetCustomAttribute<BinaryOrderAttribute>()?.Order ?? int.MaxValue,
                Key: p.GetCustomAttribute<BinaryKeyAttribute>()?.Key,
                Accessor: BuildAccessor(p)));

        var fieldCandidates = type
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly)
            .Where(f => !typeof(Delegate).IsAssignableFrom(f.FieldType))
            .Where(f => f.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Select(f => new MemberCandidate(
                f.Name,
                IsPubliclyVisible: f.IsPublic,
                HasIgnore: f.GetCustomAttribute<BinaryIgnoreAttribute>() is not null,
                HasInclude: f.GetCustomAttribute<BinaryIncludeAttribute>() is not null,
                Order: f.GetCustomAttribute<BinaryOrderAttribute>()?.Order ?? int.MaxValue,
                Key: f.GetCustomAttribute<BinaryKeyAttribute>()?.Key,
                Accessor: BuildAccessor(f)));

        var candidates = propertyCandidates.Concat(fieldCandidates).ToArray();

        return isContract ? BuildContractPlan(type, candidates) : BuildPositionalPlan(type, candidates);
    }

    private static TypeAccessorPlan BuildPositionalPlan(Type type, MemberCandidate[] candidates)
    {
        var stray = candidates.Where(c => c.HasKey).ToArray();
        if (stray.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{string.Join(", ", stray.Select(c => c.Name))}] have [BinaryKey], " +
                $"but '{type.Name}' is not marked [BinaryContract] — [BinaryKey] only applies to contract types.");

        var included = candidates
            .Where(c => !c.HasIgnore)
            .Where(c => c.IsPubliclyVisible || c.HasInclude)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => c.Accessor)
            .ToArray();

        return new TypeAccessorPlan { Type = type, Members = included, MembersByKey = null };
    }

    private static TypeAccessorPlan BuildContractPlan(Type type, MemberCandidate[] candidates)
    {
        var strayInclude = candidates.Where(c => c.HasInclude).ToArray();
        if (strayInclude.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{string.Join(", ", strayInclude.Select(c => c.Name))}] have [BinaryInclude], " +
                "which has no meaning under [BinaryContract] — [BinaryKey] already grants inclusion " +
                "regardless of visibility. Remove [BinaryInclude].");

        var strayOrder = candidates.Where(c => c.Order != int.MaxValue).ToArray();
        if (strayOrder.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' member(s) [{string.Join(", ", strayOrder.Select(c => c.Name))}] have [BinaryOrder], " +
                "which has no meaning under [BinaryContract] — order is determined by [BinaryKey] value.");

        var unmarked = candidates.Where(c => !c.HasKey && !c.HasIgnore).ToArray();
        if (unmarked.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' is [BinaryContract] — every eligible member needs exactly one of [BinaryKey(n)] " +
                $"or [BinaryIgnore]. Unmarked: [{string.Join(", ", unmarked.Select(c => c.Name))}].");

        var keyed = candidates.Where(c => c.HasKey).ToArray();

        var negativeKeys = keyed.Where(c => c.Key!.Value < 0).ToArray();
        if (negativeKeys.Length > 0)
            throw new BinaryTypeException(
                $"'{type}' has negative [BinaryKey] value(s): {string.Join(", ", negativeKeys.Select(c => c.Key!.Value))}.");

        var duplicateKeys = keyed.GroupBy(c => c.Key!.Value).Where(g => g.Count() > 1).ToArray();
        if (duplicateKeys.Length > 0)
            throw new BinaryTypeException($"'{type}' has duplicate [BinaryKey] value(s): {string.Join(", ", duplicateKeys.Select(g => g.Key))}.");

        var ordered = keyed.OrderBy(c => c.Key!.Value).Select(c => c.Accessor).ToArray();
        var byKey = keyed.ToDictionary(c => c.Key!.Value, c => c.Accessor);

        return new TypeAccessorPlan { Type = type, Members = ordered, MembersByKey = byKey };
    }

    private static MemberAccessor BuildAccessor(PropertyInfo property) => new()
    {
        Name = property.Name,
        MemberType = property.PropertyType,
        Getter = BuildPropertyGetter(property),
        Setter = BuildPropertySetter(property)
    };

    private static MemberAccessor BuildAccessor(FieldInfo field) => new()
    {
        Name = field.Name,
        MemberType = field.FieldType,
        Getter = BuildFieldGetter(field),
        Setter = BuildFieldSetter(field)
    };

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

    private sealed record MemberCandidate(
        string Name, bool IsPubliclyVisible, bool HasIgnore, bool HasInclude, int Order, int? Key, MemberAccessor Accessor)
    {
        public bool HasKey => Key is not null;
    }
}