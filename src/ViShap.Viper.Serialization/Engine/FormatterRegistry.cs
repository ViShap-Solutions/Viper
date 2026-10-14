using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace ViShap.Viper.Engine;

/// <summary>
/// Builds the codec of a declared type by reflection, once per type, for <see cref="FormatterCache{T}"/>:
/// the rule <see cref="TypeRules"/> finds for the type decides the codec, which is closed over the
/// type with <c>MakeGenericType</c> and created with <see cref="Activator"/>. The scalars and delegates
/// never reach it (<see cref="BuiltInCodecs"/>), and it is reached only while the reflection path is
/// enabled.
/// </summary>
[RequiresUnreferencedCode(ReflectionPath.UnreferencedCode)]
[RequiresDynamicCode(ReflectionPath.DynamicCode)]
internal static class FormatterRegistry
{
    /// <summary>
    /// The codec of <typeparamref name="T"/>. A type the engine cannot build a codec for resolves to
    /// one that raises <see cref="BinaryTypeException"/> wherever the type is used.
    /// </summary>
    public static Codec<T> Resolve<T>()
    {
        try
        {
            return (Codec<T>)Create(typeof(T));
        }
        catch (Exception ex) when (ex is ArgumentException or TargetInvocationException or NotSupportedException
                                      or TypeLoadException or InvalidOperationException)
        {
            return new UnsupportedCodec<T>(
                $"'{typeof(T)}' cannot be serialized: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
        }
    }

    private static object Create(Type type)
    {
        switch (TypeRules.Classify(type))
        {
            case TypeRule.Nullable:
                return Instantiate(typeof(NullableCodec<>).MakeGenericType(Nullable.GetUnderlyingType(type)!));

            case TypeRule.Enum:
                return Instantiate(
                    typeof(ScalarCodec<>).MakeGenericType(type),
                    Instantiate(typeof(EnumFormatter<>).MakeGenericType(type)));

            case TypeRule.Array:
                return CodecFor(Instantiate(typeof(ArrayView<>).MakeGenericType(type.GetElementType()!)));

            case TypeRule.MultiDimensionalArray:
                return CodecFor(Instantiate(
                    typeof(MultiDimensionalArrayFormatter<,>).MakeGenericType(type, type.GetElementType()!)));

            case TypeRule.UnsupportedArray:
                throw new NotSupportedException("An array with a non-zero lower bound has no encoding.");

            case TypeRule.Definition:
                return ForDefinition(type);

            case TypeRule.Collection:
                return ForCollection(type, TypeRules.CollectionElement(type)!);

            case TypeRule.Object:
                return Instantiate(typeof(ObjectCodec<>).MakeGenericType(type));

            default:
                throw new NotSupportedException($"'{type}' is resolved without reflection.");
        }
    }

    private static object ForDefinition(Type type)
    {
        var entry = TypeRules.Shapes[type.GetGenericTypeDefinition()];
        var arguments = type.GetGenericArguments();
        var closed = entry.Closing == TypeRules.Closing.Arguments
            ? entry.Shape.MakeGenericType(arguments)
            : entry.Shape.MakeGenericType([type, .. arguments]);

        var shape = Instantiate(closed);
        return closed.IsSubclassOf(typeof(Codec<>).MakeGenericType(type))
            ? shape
            : CodecFor(shape);
    }

    /// <summary>
    /// A custom collection, built through <c>new TCollection()</c> and its public instance
    /// <c>Add(T)</c>, both compiled once from the type.
    /// </summary>
    private static object ForCollection(Type type, Type element)
    {
        var create = Expression.Lambda(Expression.New(type)).Compile();

        var method = type.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance, [element]);
        Delegate? add = null;
        if (method is not null)
        {
            var collection = Expression.Parameter(type, "collection");
            var value = Expression.Parameter(element, "element");
            add = Expression.Lambda(Expression.Call(collection, method, value), collection, value).Compile();
        }

        return CodecFor(Instantiate(typeof(CustomCollectionShape<,>).MakeGenericType(type, element), create, add));
    }

    /// <summary>The engine's codec that drives <paramref name="shape"/>: its interface decides which.</summary>
    private static object CodecFor(object shape)
    {
        foreach (var contract in shape.GetType().GetInterfaces())
        {
            if (!contract.IsGenericType)
                continue;

            var definition = contract.GetGenericTypeDefinition();
            var arguments = contract.GetGenericArguments();

            if (definition == typeof(ISequenceShape<,,,>))
                return Instantiate(typeof(SequenceCodec<,,,>).MakeGenericType(arguments), shape);

            if (definition == typeof(IMapShape<,,,,>))
                return Instantiate(typeof(MapCodec<,,,,>).MakeGenericType(arguments), shape);

            if (definition == typeof(IArrayShape<,>))
                return Instantiate(typeof(ArrayCodec<,>).MakeGenericType(arguments), shape);

            if (definition == typeof(ICompositeFormatter<>))
                return Instantiate(typeof(CompositeCodec<>).MakeGenericType(arguments), shape);
        }

        throw new NotSupportedException($"'{shape.GetType()}' is not a shape the engine can drive.");
    }

    private static object Instantiate(Type type, params object?[] arguments) =>
        Activator.CreateInstance(type, arguments)!;
}
