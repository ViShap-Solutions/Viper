using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;

namespace ViShap.Viper.Cache;

internal static class ReadOnlySequenceAccessorCache
{
    private static readonly ConcurrentDictionary<Type, Action<BinaryPayloadWriter, object>> WriteCache = new();
    private static readonly ConcurrentDictionary<Type, Func<BinaryPayloadReader, object>> ReadCache = new();

    private static readonly MethodInfo WriteGenericDefinition =
        typeof(ReadOnlySequenceAccessorCache).GetMethod(nameof(WriteGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo ReadGenericDefinition =
        typeof(ReadOnlySequenceAccessorCache).GetMethod(nameof(ReadGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Action<BinaryPayloadWriter, object> GetWriter(Type elementType) =>
        WriteCache.GetOrAdd(elementType, static t =>
            (Action<BinaryPayloadWriter, object>)WriteGenericDefinition.MakeGenericMethod(t)
                .CreateDelegate(typeof(Action<BinaryPayloadWriter, object>)));

    public static Func<BinaryPayloadReader, object> GetReader(Type elementType) =>
        ReadCache.GetOrAdd(elementType, static t =>
            (Func<BinaryPayloadReader, object>)ReadGenericDefinition.MakeGenericMethod(t)
                .CreateDelegate(typeof(Func<BinaryPayloadReader, object>)));

    private static void WriteGeneric<T>(BinaryPayloadWriter writer, object boxedSequence)
    {
        var sequence = (ReadOnlySequence<T>)boxedSequence;
        writer.WriteInt32(checked((int)sequence.Length));

        foreach (var memory in sequence)
        {
            var span = memory.Span;
            for (int i = 0; i < span.Length; i++)
                writer.WriteElement(span[i], typeof(T));
        }
    }

    private static object ReadGeneric<T>(BinaryPayloadReader reader)
    {
        int count = 
            DeserializationGuard.ValidateCount(
                reader,
                reader.ReadInt32(),
                reader.Budget.Limits.MaxCollectionLength,
                "ReadOnlySequence length");
        
        return new ReadOnlySequence<T>(
            DeserializationGuard.ReadIntoArray<T>(
                reader,
                count));
    }
}