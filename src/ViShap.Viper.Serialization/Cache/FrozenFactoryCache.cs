using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class FrozenFactoryCache
{
    private static readonly ConcurrentDictionary<(Type Key, Type Value), Func<object, object>> DictionaryFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object>> SetFactories = new();

    public static Func<object, object> GetToFrozenDictionary(Type keyType, Type valueType) =>
        DictionaryFactories.GetOrAdd((keyType, valueType), static key =>
        {
            var method = typeof(FrozenDictionary).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "ToFrozenDictionary" && m.GetParameters().Length == 1 && m.IsGenericMethodDefinition)
                .MakeGenericMethod(key.Key, key.Value);

            var param = Expression.Parameter(typeof(object), "source");
            var call = Expression.Call(method, Expression.Convert(param, method.GetParameters()[0].ParameterType));
            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
        });

    public static Func<object, object> GetToFrozenSet(Type elementType) =>
        SetFactories.GetOrAdd(elementType, static t =>
        {
            var method = typeof(FrozenSet).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "ToFrozenSet" && m.GetParameters().Length == 1 && m.IsGenericMethodDefinition)
                .MakeGenericMethod(t);

            var param = Expression.Parameter(typeof(object), "source");
            var call = Expression.Call(method, Expression.Convert(param, method.GetParameters()[0].ParameterType));
            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
        });
}