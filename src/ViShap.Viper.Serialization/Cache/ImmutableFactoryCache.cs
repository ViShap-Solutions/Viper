using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class ImmutableFactoryCache
{
    private static readonly ConcurrentDictionary<(Type Declaring, string Method, Type Element), Func<object, object>> ArrayParamFactories = new();
    private static readonly ConcurrentDictionary<(Type Declaring, string Method, Type Element), Func<object, object>> StructParamFactories = new();

    public static Func<object, object> GetArrayFactory(Type declaringType, string methodName, Type elementType) =>
        ArrayParamFactories.GetOrAdd((declaringType, methodName, elementType), static key =>
        {
            var (declaring, method, element) = key;

            var methodInfo = declaring.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == method && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
                .Select(m => m.MakeGenericMethod(element))
                .FirstOrDefault(m => m.GetParameters() is [{ ParameterType: var pt }] && pt.IsArray && pt.GetElementType() == element)
                ?? throw new BinaryTypeException($"Could not resolve a static '{method}<T>(T[])' method on '{declaring}'.");

            var param = Expression.Parameter(typeof(object), "value");
            var call = Expression.Call(methodInfo, Expression.Convert(param, methodInfo.GetParameters()[0].ParameterType));
            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
        });
    
    public static Func<object, object> GetStructFactory(Type declaringType, string methodName, Type elementType) =>
        StructParamFactories.GetOrAdd((declaringType, methodName, elementType), static key =>
        {
            var (declaring, method, element) = key;

            var methodInfo = declaring.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == method && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
                .Select(m => m.MakeGenericMethod(element))
                .FirstOrDefault(m => m.GetParameters() is [{ ParameterType: var pt }] && !pt.IsArray)
                ?? throw new BinaryTypeException($"Could not resolve a static '{method}<T>(non-array)' method on '{declaring}'.");

            var param = Expression.Parameter(typeof(object), "value");
            var call = Expression.Call(methodInfo, Expression.Convert(param, methodInfo.GetParameters()[0].ParameterType));
            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
        });
}