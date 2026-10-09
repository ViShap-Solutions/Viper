using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ViShap.Viper.Generator;

/// <summary>
/// The diagnostics of the generator. An error is a type the serializer refuses when it describes it by
/// reflection — reported at build time instead of on first use; a warning is a type the generator
/// leaves to reflection, or a declaration that is legal but cannot be read back.
/// </summary>
internal static class Descriptors
{
    private const string Category = "ViShap.Viper";
    private const string HelpRoot = "https://github.com/ViShap-Solutions/Viper/blob/main/docs/generator.md#";

    public static readonly DiagnosticDescriptor ContextNotPartial = Error(
        "VPR001",
        "A [BinaryContext] class must be partial",
        "'{0}' carries [BinaryContext] but is not partial, nor is every type it is declared in; the generator adds the contracts to it");

    public static readonly DiagnosticDescriptor ContextBaseType = Error(
        "VPR002",
        "A [BinaryContext] class must be a non-generic, non-abstract class deriving from BinarySerializerContext",
        "'{0}' carries [BinaryContext] but {1}");

    public static readonly DiagnosticDescriptor ContextMemberConflict = Error(
        "VPR003",
        "A [BinaryContext] class must not declare what the generator writes",
        "'{0}' declares {1}, which the generator writes for a [BinaryContext] class");

    public static readonly DiagnosticDescriptor KeyWithoutContract = Error(
        "VPR004",
        "[BinaryKey] on a type that is not [BinaryContract]",
        "'{0}' member '{1}' has [BinaryKey], but '{0}' is not marked [BinaryContract]; [BinaryKey] only applies to contract types");

    public static readonly DiagnosticDescriptor IncludeWithIgnore = Error(
        "VPR005",
        "[BinaryInclude] together with [BinaryIgnore]",
        "'{0}' member '{1}' has both [BinaryInclude] and [BinaryIgnore]; a member needs at most one of them");

    public static readonly DiagnosticDescriptor DuplicateOrder = Error(
        "VPR006",
        "Duplicate [BinaryOrder] value",
        "'{0}' has [BinaryOrder({1})] on more than one member: {2}");

    public static readonly DiagnosticDescriptor IncludeOnContract = Error(
        "VPR007",
        "[BinaryInclude] on a member of a contract type",
        "'{0}' member '{1}' has [BinaryInclude], which has no meaning under [BinaryContract]; [BinaryKey] already grants inclusion regardless of visibility");

    public static readonly DiagnosticDescriptor OrderOnContract = Error(
        "VPR008",
        "[BinaryOrder] on a member of a contract type",
        "'{0}' member '{1}' has [BinaryOrder], which has no meaning under [BinaryContract]; order is determined by the [BinaryKey] value");

    public static readonly DiagnosticDescriptor KeyWithIgnore = Error(
        "VPR009",
        "[BinaryKey] together with [BinaryIgnore]",
        "'{0}' member '{1}' has both [BinaryKey] and [BinaryIgnore]; a contract member needs exactly one of them");

    public static readonly DiagnosticDescriptor UnmarkedContractMember = Error(
        "VPR010",
        "Contract member without [BinaryKey] or [BinaryIgnore]",
        "'{0}' is [BinaryContract], so member '{1}' needs exactly one of [BinaryKey(n)] or [BinaryIgnore]");

    public static readonly DiagnosticDescriptor NegativeKey = Error(
        "VPR011",
        "Negative [BinaryKey] value",
        "'{0}' member '{1}' has [BinaryKey({2})]; a key is a non-negative number");

    public static readonly DiagnosticDescriptor DuplicateKey = Error(
        "VPR012",
        "Duplicate [BinaryKey] value",
        "'{0}' has [BinaryKey({1})] on more than one member: {2}");

    public static readonly DiagnosticDescriptor DelegateMember = Error(
        "VPR013",
        "Delegate member",
        "'{0}' member '{1}' is a delegate, which carries behaviour rather than data; mark it [BinaryIgnore]");

    public static readonly DiagnosticDescriptor MemberWithoutRepresentation = Error(
        "VPR014",
        "Member type with no representation on the wire",
        "'{0}' member '{1}' is of type '{2}', which has no representation on the wire; mark it [BinaryIgnore]");

    public static readonly DiagnosticDescriptor UnionTagOutOfRange = Error(
        "VPR015",
        "[BinaryUnion] tag outside 0-255",
        "[BinaryUnion] tag {1} on '{0}' must fit in a byte (0-255)");

    public static readonly DiagnosticDescriptor DuplicateUnionTag = Error(
        "VPR016",
        "Duplicate [BinaryUnion] tag",
        "'{0}' declares [BinaryUnion] tag {1} more than once");

    public static readonly DiagnosticDescriptor UnionTypeNotAssignable = Error(
        "VPR017",
        "[BinaryUnion] type not assignable to the base",
        "[BinaryUnion] on '{0}' names '{1}', which is not assignable to '{0}'");

    public static readonly DiagnosticDescriptor AbstractWithoutUnion = Warning(
        "VPR018",
        "Abstract type or interface without [BinaryUnion]",
        "'{0}' is {1} with no [BinaryUnion] map, so a value of it cannot be read back; declare the types it may hold with [BinaryUnion]");

    public static readonly DiagnosticDescriptor DescribedByReflection = Warning(
        "VPR019",
        "Type described by reflection",
        "'{0}' gets no generated contract and is described by reflection at run time: {1}");

    private static readonly Dictionary<string, DiagnosticDescriptor> All = new[]
    {
        ContextNotPartial, ContextBaseType, ContextMemberConflict, KeyWithoutContract, IncludeWithIgnore,
        DuplicateOrder, IncludeOnContract, OrderOnContract, KeyWithIgnore, UnmarkedContractMember,
        NegativeKey, DuplicateKey, DelegateMember, MemberWithoutRepresentation, UnionTagOutOfRange,
        DuplicateUnionTag, UnionTypeNotAssignable, AbstractWithoutUnion, DescribedByReflection
    }.ToDictionary(descriptor => descriptor.Id);

    /// <summary>Every descriptor, in id order.</summary>
    public static IEnumerable<DiagnosticDescriptor> Each => All.Values.OrderBy(descriptor => descriptor.Id, System.StringComparer.Ordinal);

    public static DiagnosticDescriptor ById(string id) => All[id];

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        Create(id, title, message, DiagnosticSeverity.Error);

    private static DiagnosticDescriptor Warning(string id, string title, string message) =>
        Create(id, title, message, DiagnosticSeverity.Warning);

    private static DiagnosticDescriptor Create(string id, string title, string message, DiagnosticSeverity severity) =>
        new(id, title, message, Category, severity, isEnabledByDefault: true, helpLinkUri: HelpRoot + id.ToLowerInvariant());
}
