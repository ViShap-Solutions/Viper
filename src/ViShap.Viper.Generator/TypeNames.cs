using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ViShap.Viper.Generator;

/// <summary>
/// Spells a type the way the generated code needs it: fully qualified with <c>global::</c>, a tuple as
/// the <c>ValueTuple</c> it is at run time, and — for a generic argument — with its nullable annotation,
/// or without it for <c>typeof</c>, which refuses one.
/// </summary>
internal static class TypeNames
{
    /// <summary>The type as a generic argument, with its nullable reference annotations.</summary>
    public static string Annotated(ITypeSymbol type) => Render(type, annotate: true);

    /// <summary>The type as <c>typeof</c> takes it: no nullable reference annotation anywhere.</summary>
    public static string Plain(ITypeSymbol type) => Render(type, annotate: false);

    private static string Render(ITypeSymbol type, bool annotate)
    {
        var builder = new StringBuilder();
        Append(builder, type, annotate);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, ITypeSymbol type, bool annotate)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                Append(builder, array.ElementType, annotate);
                builder.Append('[').Append(',', array.Rank - 1).Append(']');
                break;

            case ITypeParameterSymbol parameter:
                builder.Append(parameter.Name);
                break;

            case INamedTypeSymbol named when named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T:
                Append(builder, named.TypeArguments[0], annotate);
                builder.Append('?');
                return;

            case INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying }:
                AppendNamed(builder, underlying, annotate);
                break;

            case INamedTypeSymbol named:
                AppendNamed(builder, named, annotate);
                break;

            case IDynamicTypeSymbol:
                builder.Append("object");
                break;

            default:
                builder.Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                break;
        }

        if (annotate && type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
            builder.Append('?');
    }

    private static void AppendNamed(StringBuilder builder, INamedTypeSymbol type, bool annotate)
    {
        if (Keyword(type.SpecialType) is { } keyword)
        {
            builder.Append(keyword);
            return;
        }

        if (type.ContainingType is { } container)
        {
            AppendNamed(builder, container, annotate);
            builder.Append('.');
        }
        else
        {
            builder.Append("global::");
            if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
                builder.Append(ns.ToDisplayString()).Append('.');
        }

        builder.Append(type.Name);

        if (type.TypeArguments.Length > 0)
        {
            builder.Append('<');
            for (int i = 0; i < type.TypeArguments.Length; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                Append(builder, type.TypeArguments[i], annotate);
            }

            builder.Append('>');
        }
    }

    private static string? Keyword(SpecialType type) => type switch
    {
        SpecialType.System_Object => "object",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_Char => "char",
        SpecialType.System_SByte => "sbyte",
        SpecialType.System_Byte => "byte",
        SpecialType.System_Int16 => "short",
        SpecialType.System_UInt16 => "ushort",
        SpecialType.System_Int32 => "int",
        SpecialType.System_UInt32 => "uint",
        SpecialType.System_Int64 => "long",
        SpecialType.System_UInt64 => "ulong",
        SpecialType.System_Decimal => "decimal",
        SpecialType.System_Single => "float",
        SpecialType.System_Double => "double",
        SpecialType.System_String => "string",
        _ => null
    };

    /// <summary>A name usable as part of an identifier: every character that is not a letter or a digit becomes <c>_</c>.</summary>
    public static string Identifier(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');

        return builder.ToString().Trim('_');
    }

    /// <summary>The C# keyword a type is declared with.</summary>
    public static string DeclarationKeyword(INamedTypeSymbol type) =>
        (type.IsRecord, type.TypeKind) switch
        {
            (true, TypeKind.Struct) => "record struct",
            (true, _) => "record",
            (_, TypeKind.Struct) => "struct",
            (_, TypeKind.Interface) => "interface",
            _ => "class"
        };

    /// <summary>The generic parameters of a type and of the types it is nested in, outermost first.</summary>
    public static string[] AllTypeParameters(INamedTypeSymbol type)
    {
        var chain = new System.Collections.Generic.List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
            chain.Insert(0, current);

        return chain.SelectMany(level => level.TypeParameters.Select(parameter => parameter.Name)).ToArray();
    }
}
