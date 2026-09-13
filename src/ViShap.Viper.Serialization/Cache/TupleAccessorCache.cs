using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class TupleAccessorCache
{
    private static readonly ConcurrentDictionary<Type, TupleAccessors> Cache = new();

    public static TupleAccessors GetAccessors(Type declaredType) => Cache.GetOrAdd(declaredType, Build);

    private static TupleAccessors Build(Type declaredType)
    {
        var argTypes = declaredType.GetGenericArguments();
        bool isValueTuple = declaredType.IsValueType;
        var getters = new Func<object, object?>[argTypes.Length];

        for (int i = 0; i < argTypes.Length; i++)
        {
            string memberName = i == 7 ? "Rest" : $"Item{i + 1}";
            getters[i] = isValueTuple ? BuildFieldGetter(declaredType, memberName) : BuildPropertyGetter(declaredType, memberName);
        }

        var ctor = declaredType.GetConstructor(argTypes)
            ?? throw new BinaryTypeException($"'{declaredType}' has no matching constructor.");

        return new TupleAccessors(argTypes, getters, BuildConstructor(ctor, argTypes));
    }

    private static Func<object, object?> BuildFieldGetter(Type declaredType, string fieldName)
    {
        var field = declaredType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new BinaryTypeException($"Field '{fieldName}' not found on type '{declaredType}'.");

        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var access = Expression.Field(Expression.Convert(instanceParam, declaredType), field);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), instanceParam).Compile();
    }

    private static Func<object, object?> BuildPropertyGetter(Type declaredType, string propertyName)
    {
        var property = declaredType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new BinaryTypeException($"Property '{propertyName}' not found on type '{declaredType}'.");

        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var access = Expression.Property(Expression.Convert(instanceParam, declaredType), property);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), instanceParam).Compile();
    }

    private static Func<object?[], object> BuildConstructor(ConstructorInfo ctor, Type[] argTypes)
    {
        var argsParam = Expression.Parameter(typeof(object?[]), "args");
        var ctorArgs = new Expression[argTypes.Length];
        for (int i = 0; i < argTypes.Length; i++)
            ctorArgs[i] = Expression.Convert(Expression.ArrayIndex(argsParam, Expression.Constant(i)), argTypes[i]);

        var newExpr = Expression.New(ctor, ctorArgs);
        return Expression.Lambda<Func<object?[], object>>(Expression.Convert(newExpr, typeof(object)), argsParam).Compile();
    }

    internal sealed record TupleAccessors(Type[] ArgTypes, Func<object, object?>[] Getters, Func<object?[], object> Construct);
}