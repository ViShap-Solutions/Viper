using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class ImmutableFactoryCache
{
    private static readonly ConcurrentDictionary<(Type Declaring, string Method, Type Element), Func<object, object>> ArrayFactories = new();

    public static Func<object, object> GetArrayFactory(Type declaringType, string methodName, Type elementType) =>
        ArrayFactories.GetOrAdd((declaringType, methodName, elementType), static key =>
        {
            var (declaring, method, element) = key;
            var methodInfo = declaring.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == method && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsArray)
                .MakeGenericMethod(element);

            var param = Expression.Parameter(typeof(object), "array");
            var call = Expression.Call(methodInfo, Expression.Convert(param, element.MakeArrayType()));
            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), param).Compile();
        });
}