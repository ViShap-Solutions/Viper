using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ViShap.Viper.Generator;

/// <summary>
/// Everything the generator emits for one <c>[BinaryContext]</c> class, and the diagnostics it reports
/// for it. Built of strings, numbers and equatable arrays only, so the pipeline compares two runs by
/// value and re-emits nothing whose model did not change.
/// </summary>
internal sealed record ContextModel(
    ContextHeader Header,
    bool CanEmit,
    EquatableArray<ContractModel> Contracts,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>Where a context is declared, which every file generated for it repeats.</summary>
internal sealed record ContextHeader(
    string? Namespace,
    EquatableArray<ContainingType> ContainingTypes,
    string Name,
    string HintPrefix);

/// <summary>A type a nested context is declared in: its keyword and its name.</summary>
internal sealed record ContainingType(string Keyword, string Name);

/// <summary>The contract of one member-encoded type.</summary>
internal sealed record ContractModel(
    string ClassName,
    string TypeName,
    string TypeOfName,
    bool IsValueType,
    bool IsKeyed,
    bool CanBeConstructed,
    CreateKind Create,
    string? CreateCall,
    EquatableArray<MemberModel> Members,
    EquatableArray<AccessorModel> Accessors);

/// <summary>How <c>Create</c> makes an instance.</summary>
internal enum CreateKind
{
    /// <summary><c>new T()</c>.</summary>
    New,

    /// <summary>The parameterless constructor, reached through <c>[UnsafeAccessor]</c> by <c>CreateCall</c>.</summary>
    Accessor,

    /// <summary><c>default</c>, for a struct.</summary>
    Default,

    /// <summary>The type cannot be constructed; the engine never calls <c>Create</c>.</summary>
    None
}

/// <summary>
/// One member of a contract: its description, and the expressions that read and assign it on an owner
/// named <c>value</c>.
/// </summary>
internal sealed record MemberModel(
    string Name,
    string TypeName,
    string TypeOfName,
    int? Key,
    string Get,
    string Set);

/// <summary>
/// One <c>[UnsafeAccessor]</c> method a contract declares, already rendered; <see cref="GenericClass"/>
/// names the generic class it belongs in when the member's declaring type is generic.
/// </summary>
internal sealed record AccessorModel(string Declaration, string? GenericClass, string? GenericParameters);

/// <summary>A diagnostic, held as values so a model that carries it stays equatable.</summary>
internal sealed record DiagnosticInfo(string Id, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(
            Descriptors.ById(Id),
            Location?.ToLocation(),
            Arguments.Select(argument => (object?)argument).ToArray());
}

/// <summary>A source location, held as values.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true } && location.SourceTree is { } tree
            ? new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span)
            : null;
}
