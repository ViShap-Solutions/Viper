using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class MethodInvokerCache
{
    private static readonly ConcurrentDictionary<(Type Type, string Method, Type Arg), Action<object, object?>> OneArgInvokers = new();
    private static readonly ConcurrentDictionary<(Type Type, string Method, Type Arg1, Type Arg2), Action<object, object?, object?>> TwoArgInvokers = new();
    private static readonly ConcurrentDictionary<(Type Type, string Method), Func<object>> StaticFactoryInvokers = new();
    private static readonly ConcurrentDictionary<(Type Type, string Method), Func<object, object>> InstanceFinalizerInvokers = new();

    public static Action<object, object?> GetOneArgInvoker(Type type, string methodName, Type argType) =>
        OneArgInvokers.GetOrAdd((type, methodName, argType), static key =>
        {
            var (t, m, a) = key;
            var method = t.GetMethod(m, BindingFlags.Public | BindingFlags.Instance, [a])
                ?? throw new BinaryTypeException($"'{t}' has no public instance method {m}({a.Name}).");

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var argParam = Expression.Parameter(typeof(object), "arg");
            var call = Expression.Call(Expression.Convert(instanceParam, t), method, Expression.Convert(argParam, a));

            return Expression.Lambda<Action<object, object?>>(call, instanceParam, argParam).Compile();
        });

    public static Action<object, object?, object?> GetTwoArgInvoker(Type type, string methodName, Type arg1Type, Type arg2Type) =>
        TwoArgInvokers.GetOrAdd((type, methodName, arg1Type, arg2Type), static key =>
        {
            var (t, m, a1, a2) = key;
            var method = t.GetMethod(m, BindingFlags.Public | BindingFlags.Instance, [a1, a2])
                ?? throw new BinaryTypeException($"'{t}' has no public instance method {m}({a1.Name}, {a2.Name}).");

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var arg1Param = Expression.Parameter(typeof(object), "arg1");
            var arg2Param = Expression.Parameter(typeof(object), "arg2");
            var call = Expression.Call(
                Expression.Convert(instanceParam, t),
                method,
                Expression.Convert(arg1Param, a1),
                Expression.Convert(arg2Param, a2));

            return Expression.Lambda<Action<object, object?, object?>>(call, instanceParam, arg1Param, arg2Param).Compile();
        });

    public static Func<object> GetStaticFactoryInvoker(Type type, string methodName) =>
        StaticFactoryInvokers.GetOrAdd((type, methodName), static key =>
        {
            var (t, m) = key;
            var method = t.GetMethod(m, BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
                ?? throw new BinaryTypeException($"'{t}' has no public static parameterless method {m}().");

            var call = Expression.Call(method);
            return Expression.Lambda<Func<object>>(Expression.Convert(call, typeof(object))).Compile();
        });

    public static Func<object, object> GetInstanceFinalizerInvoker(Type type, string methodName) =>
        InstanceFinalizerInvokers.GetOrAdd((type, methodName), static key =>
        {
            var (t, m) = key;
            var method = t.GetMethod(m, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)
                ?? throw new BinaryTypeException($"'{t}' has no public instance parameterless method {m}().");

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var call = Expression.Call(Expression.Convert(instanceParam, t), method);

            return Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), instanceParam).Compile();
        });
}