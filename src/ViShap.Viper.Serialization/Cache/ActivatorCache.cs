using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace ViShap.Viper.Cache;

internal static class ActivatorCache
{
    private static readonly ConcurrentDictionary<Type, Func<object>> Parameterless = new();
    private static readonly ConcurrentDictionary<(Type Type, Type Arg), Func<object?, object>> OneArg = new();
    private static readonly ConcurrentDictionary<(Type Type, Type Arg1, Type Arg2), Func<object?, object?, object>> TwoArg = new();

    public static object CreateInstance(Type concreteType) =>
        Parameterless.GetOrAdd(concreteType, static t =>
            Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(t), typeof(object))).Compile())();

    public static Func<object?, object> GetOneArgConstructor(Type type, Type argType) =>
        OneArg.GetOrAdd((type, argType), static key =>
        {
            var (t, a) = key;
            var ctor = t.GetConstructor([a])
                ?? throw new BinaryTypeException($"'{t}' has no constructor({a.Name}).");

            var p = Expression.Parameter(typeof(object), "arg");
            var newExpr = Expression.New(ctor, Expression.Convert(p, a));
            return Expression.Lambda<Func<object?, object>>(Expression.Convert(newExpr, typeof(object)), p).Compile();
        });

    public static Func<object?, object?, object> GetTwoArgConstructor(Type type, Type arg1Type, Type arg2Type) =>
        TwoArg.GetOrAdd((type, arg1Type, arg2Type), static key =>
        {
            var (t, a1, a2) = key;
            var ctor = t.GetConstructor([a1, a2])
                ?? throw new BinaryTypeException($"'{t}' has no constructor({a1.Name}, {a2.Name}).");

            var p1 = Expression.Parameter(typeof(object), "arg1");
            var p2 = Expression.Parameter(typeof(object), "arg2");
            var newExpr = Expression.New(ctor, Expression.Convert(p1, a1), Expression.Convert(p2, a2));
            return Expression.Lambda<Func<object?, object?, object>>(Expression.Convert(newExpr, typeof(object)), p1, p2).Compile();
        });
}