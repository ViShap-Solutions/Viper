using System.Collections.Concurrent;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class LazyAccessorCache
{
    private static readonly ConcurrentDictionary<Type, Func<object?, object>> Factories = new();

    private static readonly MethodInfo CreateLazyGenericMethod =
        typeof(LazyAccessorCache).GetMethod(nameof(CreateLazyGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Func<object, object> GetValueGetter(Type declaredType) => 
        MethodInvokerCache.GetInstanceFinalizerInvoker(declaredType, "get_Value");

    public static Func<object?, object> GetFactory(Type elementType) =>
        Factories.GetOrAdd(elementType, static t =>
            (Func<object?, object>)CreateLazyGenericMethod.MakeGenericMethod(t)
                .CreateDelegate(typeof(Func<object?, object>)));

    private static object CreateLazyGeneric<T>(object? value)
    {
        T typedValue = (T)value!;
        return new Lazy<T>(() => typedValue);
    }
}