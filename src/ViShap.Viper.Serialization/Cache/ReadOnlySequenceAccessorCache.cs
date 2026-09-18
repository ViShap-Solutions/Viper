using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;

namespace ViShap.Viper.Cache;

/// <summary>
/// <c>ReadOnlySequence&lt;T&gt;</c> is not <c>IEnumerable</c>, so its elements are reached through a
/// cached generic delegate rather than reflection on every call.
/// </summary>
internal static class ReadOnlySequenceAccessorCache
{
    private static readonly ConcurrentDictionary<Type, Func<object, object>> ToArrayCache = new();

    private static readonly MethodInfo ToArrayDefinition =
        typeof(ReadOnlySequenceAccessorCache)
            .GetMethod(nameof(ToArrayGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Func<object, object> GetToArray(Type elementType) =>
        ToArrayCache.GetOrAdd(elementType, static type =>
            (Func<object, object>)ToArrayDefinition
                .MakeGenericMethod(type)
                .CreateDelegate(typeof(Func<object, object>)));

    private static object ToArrayGeneric<T>(object boxedSequence) =>
        ((ReadOnlySequence<T>)boxedSequence).ToArray();
}
