using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ViShap.Viper.Generator;

/// <summary>
/// How the serializer encodes a declared type, decided by the rules the serializer applies, in its
/// order: delegates, nullable values, the scalars and enums, arrays, the supported generic definitions,
/// concrete collections with a public parameterless constructor, and last every other type, which is
/// member-encoded through a type contract. The generator uses it only to find the member-encoded types
/// a context reaches; it never encodes anything itself.
/// </summary>
internal static class TypeShapes
{
    /// <summary>The scalars, by metadata name: the types the serializer encodes as one self-contained value.</summary>
    public static readonly IReadOnlyCollection<string> Scalars = new SortedSet<string>(System.StringComparer.Ordinal)
    {
        "System.Boolean", "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32",
        "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal",
        "System.Char", "System.String", "System.Half", "System.Int128", "System.UInt128", "System.IntPtr",
        "System.UIntPtr", "System.Text.Rune", "System.Numerics.BigInteger",
        "System.DateTime", "System.DateTimeOffset", "System.TimeSpan", "System.DateOnly", "System.TimeOnly",
        "System.TimeZoneInfo",
        "System.Numerics.Complex", "System.Numerics.Vector2", "System.Numerics.Vector3", "System.Numerics.Vector4",
        "System.Numerics.Quaternion", "System.Numerics.Plane", "System.Numerics.Matrix3x2", "System.Numerics.Matrix4x4",
        "System.Guid", "System.Uri", "System.Version", "System.Text.StringBuilder", "System.Globalization.CultureInfo",
        "System.Collections.BitArray"
    };

    /// <summary>The supported generic definitions, by metadata name: composites, sequences and maps.</summary>
    public static readonly IReadOnlyCollection<string> GenericDefinitions = new SortedSet<string>(System.StringComparer.Ordinal)
    {
        "System.Collections.Generic.KeyValuePair`2", "System.Lazy`1",
        "System.Tuple`1", "System.Tuple`2", "System.Tuple`3", "System.Tuple`4", "System.Tuple`5", "System.Tuple`6",
        "System.Tuple`7", "System.Tuple`8",
        "System.ValueTuple`1", "System.ValueTuple`2", "System.ValueTuple`3", "System.ValueTuple`4", "System.ValueTuple`5",
        "System.ValueTuple`6", "System.ValueTuple`7", "System.ValueTuple`8",
        "System.Collections.Immutable.ImmutableArray`1", "System.Memory`1", "System.ReadOnlyMemory`1", "System.ArraySegment`1",
        "System.Buffers.ReadOnlySequence`1", "System.Collections.Generic.List`1", "System.Collections.Generic.IList`1",
        "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IReadOnlyList`1", "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.HashSet`1", "System.Collections.Generic.ISet`1", "System.Collections.Generic.SortedSet`1",
        "System.Collections.Generic.LinkedList`1", "System.Collections.ObjectModel.ObservableCollection`1",
        "System.Collections.Generic.Stack`1", "System.Collections.Generic.Queue`1",
        "System.Collections.Concurrent.ConcurrentBag`1", "System.Collections.Concurrent.ConcurrentQueue`1",
        "System.Collections.Concurrent.ConcurrentStack`1", "System.Collections.ObjectModel.ReadOnlyObservableCollection`1",
        "System.Collections.ObjectModel.ReadOnlyCollection`1", "System.Collections.Immutable.ImmutableList`1",
        "System.Collections.Immutable.IImmutableList`1", "System.Collections.Immutable.ImmutableHashSet`1",
        "System.Collections.Immutable.IImmutableSet`1", "System.Collections.Immutable.ImmutableSortedSet`1",
        "System.Collections.Immutable.ImmutableQueue`1", "System.Collections.Immutable.IImmutableQueue`1",
        "System.Collections.Immutable.ImmutableStack`1", "System.Collections.Immutable.IImmutableStack`1",
        "System.Collections.Frozen.FrozenSet`1",
        "System.Collections.ObjectModel.ReadOnlyDictionary`2", "System.Collections.Generic.IReadOnlyDictionary`2",
        "System.Collections.Frozen.FrozenDictionary`2", "System.Collections.Immutable.ImmutableDictionary`2",
        "System.Collections.Immutable.IImmutableDictionary`2", "System.Collections.Immutable.ImmutableSortedDictionary`2",
        "System.Collections.Concurrent.ConcurrentDictionary`2", "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.SortedList`2", "System.Collections.Generic.PriorityQueue`2",
        "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.IDictionary`2"
    };

    /// <summary>
    /// The types a declared type's codec encodes through: nothing for a scalar, an enum or a delegate;
    /// the element, argument or underlying types for an array, a supported generic definition, a nullable
    /// value or a concrete collection; and the type itself when it is member-encoded.
    /// </summary>
    public static Shape Classify(ITypeSymbol type, out IEnumerable<ITypeSymbol> children)
    {
        children = Enumerable.Empty<ITypeSymbol>();

        switch (type)
        {
            case IArrayTypeSymbol array:
                children = new[] { array.ElementType };
                return Shape.Container;

            case IPointerTypeSymbol or IFunctionPointerTypeSymbol or ITypeParameterSymbol or IErrorTypeSymbol:
                return Shape.Unsupported;

            case INamedTypeSymbol named:
                if (named.TypeKind == TypeKind.Delegate || IsDelegateBase(named))
                    return Shape.Unsupported;

                if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    children = new[] { named.TypeArguments[0] };
                    return Shape.Container;
                }

                if (named.TypeKind == TypeKind.Enum || Scalars.Contains(MetadataName(named)))
                    return Shape.Scalar;

                if (named.IsGenericType && GenericDefinitions.Contains(MetadataName(named.OriginalDefinition)))
                {
                    children = named.TypeArguments;
                    return Shape.Container;
                }

                if (CollectionElements(named) is { Count: > 0 } elements)
                {
                    children = elements;
                    return Shape.Container;
                }

                return named.SpecialType == SpecialType.System_Object ? Shape.Unsupported : Shape.Object;

            default:
                return Shape.Unsupported;
        }
    }

    /// <summary>A type's metadata name with its namespace and containing types: <c>System.Collections.Generic.List`1</c>.</summary>
    public static string MetadataName(INamedTypeSymbol type)
    {
        string name = type.MetadataName;
        for (var container = type.ContainingType; container is not null; container = container.ContainingType)
            name = container.MetadataName + "+" + name;

        return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + name : name;
    }

    private static bool IsDelegateBase(INamedTypeSymbol type) =>
        type.SpecialType is SpecialType.System_Delegate or SpecialType.System_MulticastDelegate;

    /// <summary>
    /// The element types of a concrete collection the serializer builds: not an interface, not abstract,
    /// implementing <c>ICollection&lt;T&gt;</c>, with a public parameterless constructor it declares.
    /// </summary>
    private static List<ITypeSymbol>? CollectionElements(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface || type.IsAbstract)
            return null;

        bool constructible = type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0 &&
            constructor.DeclaredAccessibility == Accessibility.Public &&
            !(type.IsValueType && constructor.IsImplicitlyDeclared));

        if (!constructible)
            return null;

        return type.AllInterfaces
            .Where(contract => contract.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T)
            .Select(contract => contract.TypeArguments[0])
            .ToList();
    }
}

/// <summary>What a declared type is to the serializer.</summary>
internal enum Shape
{
    /// <summary>A scalar or an enum: no children, no contract.</summary>
    Scalar,

    /// <summary>An array, a nullable value, a supported generic definition or a concrete collection: its children are encoded.</summary>
    Container,

    /// <summary>A member-encoded type, which has a type contract.</summary>
    Object,

    /// <summary>A delegate, a pointer, a type parameter, <see cref="object"/> or a broken type: nothing the generator can write a contract for.</summary>
    Unsupported
}
