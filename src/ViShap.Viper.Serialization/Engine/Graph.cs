using System.Diagnostics.CodeAnalysis;

namespace ViShap.Viper.Engine;

/// <summary>
/// The engine's entry for one payload: opens its traversal — reference framing, reference tables,
/// the ancestor stack — hands the root to its codec, and closes the traversal again, returning every
/// pooled part cleared whether the payload was read or not.
/// </summary>
internal static class Graph
{
    /// <summary>Writes <paramref name="value"/> as the root of a payload.</summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static void WriteRoot<T>(ref WireWriter writer, T value, bool preserveReferences)
    {
        writer.State.Graph.BeginWrite(preserveReferences);
        try
        {
            FormatterCache<T>.Instance.Write(ref writer, value);
        }
        finally
        {
            writer.State.Graph.End();
        }
    }

    /// <summary>
    /// Reads the root of a payload. A <paramref name="target"/> that is not <see langword="null"/> is
    /// populated instead of a new instance being created; only a member-encoded class can be.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
    [RequiresDynamicCode(ReflectionPath.DynamicCode)]
    public static T? ReadRoot<T>(ref WireReader reader, T? target, bool preserveReferences)
    {
        reader.State.Graph.BeginRead(preserveReferences);
        try
        {
            if (typeof(T).IsValueType || target is null)
                return FormatterCache<T>.Instance.Read(ref reader);

            if (FormatterCache<T>.Instance is not ObjectCodec<T> codec)
                throw new BinaryTypeException(
                    $"Populate-in-place is only supported for member-encoded types; '{typeof(T)}' is " +
                    $"encoded as a {FormatterCache<T>.Instance.Shape.ToString().ToLowerInvariant()}. Use a " +
                    "Deserialize overload instead.");

            codec.Populate(ref reader, target);
            return target;
        }
        finally
        {
            reader.State.Graph.End();
        }
    }
}
