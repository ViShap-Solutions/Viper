using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ViShap.Viper.Generator;

/// <summary>
/// Turns one <c>[BinaryContext]</c> class into its model: checks the class, walks every type its
/// attributes list and every member-encoded type those reach, and for each describes the contract the
/// serializer would build by reflection — the same members, in the same total order, with the same keys,
/// and the same rejections — together with how generated code reaches each member.
/// </summary>
internal sealed class Planner
{
    private const string AttributeNamespace = "ViShap.Viper.";

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _context;
    private readonly CancellationToken _cancellation;
    private readonly List<DiagnosticInfo> _diagnostics = [];
    private readonly List<(ITypeSymbol Type, ContractModel Contract)> _contracts = [];
    private readonly HashSet<ITypeSymbol> _seen = new(SymbolEqualityComparer.Default);
    private readonly Queue<(ITypeSymbol Type, Location? Origin)> _pending = new();

    private readonly INamedTypeSymbol? _contractAttribute;
    private readonly INamedTypeSymbol? _keyAttribute;
    private readonly INamedTypeSymbol? _orderAttribute;
    private readonly INamedTypeSymbol? _includeAttribute;
    private readonly INamedTypeSymbol? _ignoreAttribute;
    private readonly INamedTypeSymbol? _unionAttribute;
    private readonly INamedTypeSymbol? _compilerGenerated;
    private readonly INamedTypeSymbol? _obsolete;

    private Planner(Compilation compilation, INamedTypeSymbol context, CancellationToken cancellation)
    {
        _compilation = compilation;
        _context = context;
        _cancellation = cancellation;

        _contractAttribute = compilation.GetTypeByMetadataName(AttributeNamespace + "BinaryContractAttribute");
        _keyAttribute = compilation.GetTypeByMetadataName(AttributeNamespace + "BinaryKeyAttribute");
        _orderAttribute = compilation.GetTypeByMetadataName(AttributeNamespace + "BinaryOrderAttribute");
        _includeAttribute = compilation.GetTypeByMetadataName(AttributeNamespace + "BinaryIncludeAttribute");
        _ignoreAttribute = compilation.GetTypeByMetadataName(AttributeNamespace + "BinaryIgnoreAttribute");
        _unionAttribute = compilation.GetTypeByMetadataName(AttributeNamespace + "BinaryUnionAttribute");
        _compilerGenerated = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        _obsolete = compilation.GetTypeByMetadataName("System.ObsoleteAttribute");
    }

    /// <summary>The model of the <c>[BinaryContext]</c> class <paramref name="context"/>.</summary>
    public static ContextModel Plan(
        INamedTypeSymbol context,
        IEnumerable<AttributeData> attributes,
        Compilation compilation,
        CancellationToken cancellation)
    {
        var planner = new Planner(compilation, context, cancellation);
        bool canEmit = planner.CheckContext();

        foreach (var attribute in attributes)
        {
            var origin = attribute.ApplicationSyntaxReference?.GetSyntax(cancellation).GetLocation();
            foreach (var argument in attribute.ConstructorArguments)
            {
                var roots = argument.Kind == TypedConstantKind.Array ? argument.Values : [argument];
                foreach (var root in roots)
                {
                    if (root.Value is ITypeSymbol type)
                        planner._pending.Enqueue((type, origin));
                }
            }
        }

        while (planner._pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var (type, origin) = planner._pending.Dequeue();
            planner.Visit(type, origin);
        }

        var header = new ContextHeader(
            context.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
            new EquatableArray<ContainingType>(Containers(context)
                .Select(container => new ContainingType(TypeNames.DeclarationKeyword(container), container.Name))),
            context.Name,
            TypeNames.Identifier(TypeNames.Plain(context).Replace("global::", string.Empty)));

        return new ContextModel(
            header,
            canEmit,
            new EquatableArray<ContractModel>(planner.NameContracts()),
            new EquatableArray<DiagnosticInfo>(planner._diagnostics));
    }

    // --- the context class --------------------------------------------------------------------------

    private bool CheckContext()
    {
        bool valid = true;
        var location = _context.Locations.FirstOrDefault();

        bool partial = _context.DeclaringSyntaxReferences.All(IsPartial) &&
                       Containers(_context).All(container => container.DeclaringSyntaxReferences.All(IsPartial));
        if (!partial)
        {
            Report(Descriptors.ContextNotPartial, location, _context.ToDisplayString());
            valid = false;
        }

        string? problem =
            _context.TypeKind != TypeKind.Class ? "is not a class" :
            _context.IsAbstract || _context.IsStatic ? "is abstract or static" :
            _context.IsGenericType || Containers(_context).Any(container => container.IsGenericType) ? "is generic, or nested in a generic type" :
            !DerivesFromContext(_context) ? "does not derive from ViShap.Viper.BinarySerializerContext" :
            null;

        if (problem is not null)
        {
            Report(Descriptors.ContextBaseType, location, _context.ToDisplayString(), problem);
            valid = false;
        }

        if (_context.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 && !constructor.IsImplicitlyDeclared))
        {
            Report(Descriptors.ContextMemberConflict, location, _context.ToDisplayString(), "a parameterless constructor");
            valid = false;
        }

        if (_context.GetMembers("Default").Length > 0)
        {
            Report(Descriptors.ContextMemberConflict, location, _context.ToDisplayString(), "a member named 'Default'");
            valid = false;
        }

        return valid;
    }

    private static bool IsPartial(SyntaxReference reference) =>
        reference.GetSyntax() is TypeDeclarationSyntax declaration &&
        declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));

    private bool DerivesFromContext(INamedTypeSymbol type)
    {
        var context = _compilation.GetTypeByMetadataName("ViShap.Viper.BinarySerializerContext");
        for (var level = type.BaseType; level is not null; level = level.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(level, context))
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> Containers(INamedTypeSymbol type)
    {
        var containers = new List<INamedTypeSymbol>();
        for (var container = type.ContainingType; container is not null; container = container.ContainingType)
            containers.Insert(0, container);

        return containers;
    }

    // --- the walk -----------------------------------------------------------------------------------

    private void Visit(ITypeSymbol type, Location? origin)
    {
        if (!_seen.Add(type))
            return;

        switch (TypeShapes.Classify(type, out var children))
        {
            case Shape.Container:
                foreach (var child in children)
                    _pending.Enqueue((child, origin));
                break;

            case Shape.Object:
                PlanObject((INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), origin);
                break;
        }
    }

    private void PlanObject(INamedTypeSymbol type, Location? origin)
    {
        bool hasUnion = CheckUnion(type);

        if ((type.IsAbstract || type.TypeKind == TypeKind.Interface) && !hasUnion && !type.IsStatic)
            Report(Descriptors.AbstractWithoutUnion, origin, Display(type),
                type.TypeKind == TypeKind.Interface ? "an interface" : "abstract");

        if (Unreachable(type) is { } reason)
        {
            Report(Descriptors.DescribedByReflection, origin, Display(type), reason);
            return;
        }

        var candidates = Candidates(type, out string? hidden);
        if (hidden is not null)
        {
            Report(Descriptors.DescribedByReflection, origin, Display(type), hidden);
            return;
        }

        bool keyed = IsContract(type);
        var members = keyed ? Keyed(type, candidates) : Positional(type, candidates);
        if (members is null)
            return;

        foreach (var member in members)
            _pending.Enqueue((member.Type, member.Location ?? origin));

        if (MemberObstacle(type, members) is { } obstacle)
        {
            Report(Descriptors.DescribedByReflection, origin, Display(type), obstacle);
            return;
        }

        _contracts.Add((type, Contract(type, keyed, members)));
    }

    /// <summary>Why generated code cannot describe <paramref name="type"/>, or <see langword="null"/> when it can.</summary>
    private string? Unreachable(INamedTypeSymbol type)
    {
        if (!type.Locations.Any(location => location.IsInSource))
            return "it is declared in another assembly, whose non-public members the generator cannot see";

        if (type.IsUnboundGenericType || ContainsTypeParameters(type))
            return "it is an open generic type";

        if (type.IsStatic)
            return "it is a static class";

        if (type.IsRefLikeType)
            return "it is a ref struct";

        if (!IsAccessible(type))
            return $"it is not accessible from '{_context.ToDisplayString()}'";

        for (var level = type.BaseType; level is not null; level = level.BaseType)
        {
            if (level.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
                break;

            if (!level.Locations.Any(location => location.IsInSource))
                return $"it derives from '{Display(level)}', declared in another assembly, whose non-public members the generator cannot see";
        }

        return null;
    }

    private string? MemberObstacle(INamedTypeSymbol type, IReadOnlyList<Candidate> members)
    {
        foreach (var member in members)
        {
            if (member.Symbol is IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 })
                return $"member '{member.Name}' implements an interface explicitly";

            if (member.Symbol is IFieldSymbol { IsFixedSizeBuffer: true })
                return $"member '{member.Name}' is a fixed-size buffer";

            if (!IsAccessible(member.Type))
                return $"member '{member.Name}' is of type '{Display(member.Type)}', which is not accessible from '{_context.ToDisplayString()}'";

            if (!SymbolEqualityComparer.Default.Equals(member.Declaring, type) && !IsAccessible(member.Declaring))
                return $"member '{member.Name}' is declared on '{Display(member.Declaring)}', which is not accessible from '{_context.ToDisplayString()}'";
        }

        return null;
    }

    // --- unions ---------------------------------------------------------------------------------------

    /// <summary>Checks the <c>[BinaryUnion]</c> map declared on <paramref name="type"/> and walks its arms.</summary>
    private bool CheckUnion(INamedTypeSymbol type)
    {
        var unions = type.GetAttributes().Where(attribute => Is(attribute, _unionAttribute)).ToArray();
        if (unions.Length == 0)
            return false;

        var seenTags = new HashSet<int>();
        var reportedTags = new HashSet<int>();

        foreach (var union in unions)
        {
            var location = union.ApplicationSyntaxReference?.GetSyntax(_cancellation).GetLocation();
            if (union.ConstructorArguments.Length != 2 || union.ConstructorArguments[0].Value is not int tag)
                continue;

            if (tag is < 0 or > 255)
                Report(Descriptors.UnionTagOutOfRange, location, Display(type), tag.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!seenTags.Add(tag) && reportedTags.Add(tag))
                Report(Descriptors.DuplicateUnionTag, location, Display(type), tag.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (union.ConstructorArguments[1].Value is not ITypeSymbol arm)
                continue;

            if (!IsAssignable(arm, type))
                Report(Descriptors.UnionTypeNotAssignable, location, Display(type), Display(arm));
            else
                _pending.Enqueue((arm, location));
        }

        return true;
    }

    private bool IsAssignable(ITypeSymbol from, INamedTypeSymbol to)
    {
        if (SymbolEqualityComparer.Default.Equals(from, to))
            return true;

        var conversion = _compilation.ClassifyCommonConversion(from, to);
        return conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference);
    }

    // --- the member plan, as ReflectedContract builds it ----------------------------------------------

    /// <summary>One member the plan may include, with everything the rules read from it.</summary>
    private sealed record Candidate(
        int Distance,
        string Name,
        bool IsPubliclyVisible,
        bool HasIgnore,
        bool HasInclude,
        int Order,
        int? Key,
        ISymbol Symbol,
        ITypeSymbol Type,
        INamedTypeSymbol Declaring,
        Location? Location);

    /// <summary>
    /// The members of every level of the hierarchy, most derived first, as the serializer collects them:
    /// read/write properties that are not indexers, each override once at its most derived declaration,
    /// and fields that are neither read-only nor compiler-generated, whatever their visibility.
    /// </summary>
    private List<Candidate> Candidates(INamedTypeSymbol type, out string? hidden)
    {
        hidden = null;
        var candidates = new List<Candidate>();
        var claimed = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        int distance = 0;

        for (var level = type; level is not null; level = level.BaseType, distance++)
        {
            if (level.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
                break;

            foreach (var member in level.GetMembers())
            {
                if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false } property)
                    continue;

                if (property.GetMethod is null || property.SetMethod is null)
                    continue;

                if (!claimed.Add(BaseDefinition(property.GetMethod)))
                    continue;

                candidates.Add(Describe(
                    distance, property, property.Type, level,
                    property.GetMethod.DeclaredAccessibility == Accessibility.Public,
                    attribute => FindOnProperty(property, attribute)));
            }

            foreach (var member in level.GetMembers())
            {
                if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field)
                    continue;

                if (field.IsReadOnly || field.IsImplicitlyDeclared || Find(field, _compilerGenerated) is not null)
                    continue;

                candidates.Add(Describe(
                    distance, field, field.Type, level,
                    field.DeclaredAccessibility == Accessibility.Public,
                    attribute => Find(field, attribute)));
            }
        }

        return candidates;
    }

    private Candidate Describe(
        int distance,
        ISymbol member,
        ITypeSymbol memberType,
        INamedTypeSymbol level,
        bool isPublic,
        System.Func<INamedTypeSymbol?, AttributeData?> find) =>
        new(
            distance,
            member.Name,
            isPublic,
            find(_ignoreAttribute) is not null,
            find(_includeAttribute) is not null,
            IntArgument(find(_orderAttribute)) ?? int.MaxValue,
            IntArgument(find(_keyAttribute)),
            member,
            memberType,
            level,
            member.Locations.FirstOrDefault(location => location.IsInSource));

    private List<Candidate>? Positional(INamedTypeSymbol type, List<Candidate> candidates)
    {
        bool valid = true;

        foreach (var stray in candidates.Where(candidate => candidate.Key is not null))
        {
            Report(Descriptors.KeyWithoutContract, stray.Location, Display(type), stray.Name);
            valid = false;
        }

        foreach (var contradictory in candidates.Where(candidate => candidate.HasIgnore && candidate.HasInclude))
        {
            Report(Descriptors.IncludeWithIgnore, contradictory.Location, Display(type), contradictory.Name);
            valid = false;
        }

        var eligible = candidates
            .Where(candidate => !candidate.HasIgnore)
            .Where(candidate => candidate.IsPubliclyVisible || candidate.HasInclude)
            .ToList();

        valid &= CheckMemberTypes(type, eligible);

        foreach (var group in eligible.Where(candidate => candidate.Order != int.MaxValue).GroupBy(candidate => candidate.Order))
        {
            if (group.Count() > 1)
            {
                Report(Descriptors.DuplicateOrder, group.First().Location, Display(type),
                    group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), Names(group));
                valid = false;
            }
        }

        if (!valid)
            return null;

        // Order, then ordinal name, then the declaring level with the base first: a total order.
        return eligible
            .OrderBy(candidate => candidate.Order)
            .ThenBy(candidate => candidate.Name, System.StringComparer.Ordinal)
            .ThenByDescending(candidate => candidate.Distance)
            .ToList();
    }

    private List<Candidate>? Keyed(INamedTypeSymbol type, List<Candidate> candidates)
    {
        bool valid = true;

        foreach (var stray in candidates.Where(candidate => candidate.HasInclude))
        {
            Report(Descriptors.IncludeOnContract, stray.Location, Display(type), stray.Name);
            valid = false;
        }

        foreach (var stray in candidates.Where(candidate => candidate.Order != int.MaxValue))
        {
            Report(Descriptors.OrderOnContract, stray.Location, Display(type), stray.Name);
            valid = false;
        }

        foreach (var contradictory in candidates.Where(candidate => candidate.Key is not null && candidate.HasIgnore))
        {
            Report(Descriptors.KeyWithIgnore, contradictory.Location, Display(type), contradictory.Name);
            valid = false;
        }

        foreach (var unmarked in candidates.Where(candidate => candidate.Key is null && !candidate.HasIgnore))
        {
            Report(Descriptors.UnmarkedContractMember, unmarked.Location, Display(type), unmarked.Name);
            valid = false;
        }

        var keyed = candidates.Where(candidate => candidate.Key is not null).ToList();

        valid &= CheckMemberTypes(type, keyed);

        foreach (var negative in keyed.Where(candidate => candidate.Key < 0))
        {
            Report(Descriptors.NegativeKey, negative.Location, Display(type), negative.Name,
                negative.Key!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            valid = false;
        }

        foreach (var group in keyed.GroupBy(candidate => candidate.Key!.Value))
        {
            if (group.Count() > 1)
            {
                Report(Descriptors.DuplicateKey, group.First().Location, Display(type),
                    group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), Names(group));
                valid = false;
            }
        }

        return valid ? keyed.OrderBy(candidate => candidate.Key!.Value).ToList() : null;
    }

    /// <summary>Rejects a member that is a delegate, or whose type has no representation on the wire.</summary>
    private bool CheckMemberTypes(INamedTypeSymbol type, IEnumerable<Candidate> members)
    {
        bool valid = true;

        foreach (var member in members)
        {
            if (member.Type is INamedTypeSymbol named &&
                (named.TypeKind == TypeKind.Delegate || named.SpecialType is SpecialType.System_Delegate or SpecialType.System_MulticastDelegate))
            {
                Report(Descriptors.DelegateMember, member.Location, Display(type), member.Name);
                valid = false;
            }
            else if (member.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol || member.Type.IsRefLikeType)
            {
                Report(Descriptors.MemberWithoutRepresentation, member.Location, Display(type), member.Name, Display(member.Type));
                valid = false;
            }
        }

        return valid;
    }

    private bool IsContract(INamedTypeSymbol type)
    {
        for (var level = type; level is not null; level = level.BaseType)
        {
            if (Find(level, _contractAttribute) is not null)
                return true;
        }

        return false;
    }

    // --- the contract ---------------------------------------------------------------------------------

    private ContractModel Contract(INamedTypeSymbol type, bool keyed, IReadOnlyList<Candidate> candidates)
    {
        var accessors = new List<AccessorModel>();
        var genericClasses = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        var members = new List<MemberModel>();

        for (int index = 0; index < candidates.Count; index++)
            members.Add(Member(type, candidates[index], index, accessors, genericClasses));

        var (create, createCall, canBeConstructed) = Construction(type, accessors, genericClasses);

        return new ContractModel(
            ClassName: string.Empty,
            TypeName: TypeNames.Annotated(type),
            TypeOfName: TypeNames.Plain(type),
            IsValueType: type.IsValueType,
            IsKeyed: keyed,
            CanBeConstructed: canBeConstructed,
            Create: create,
            CreateCall: createCall,
            Members: new EquatableArray<MemberModel>(members),
            Accessors: new EquatableArray<AccessorModel>(accessors));
    }

    private (CreateKind Create, string? Call, bool CanBeConstructed) Construction(
        INamedTypeSymbol type,
        List<AccessorModel> accessors,
        Dictionary<INamedTypeSymbol, string> genericClasses)
    {
        if (type.IsValueType)
            return (CreateKind.Default, null, true);

        if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
            return (CreateKind.None, null, false);

        var constructor = type.InstanceConstructors.FirstOrDefault(candidate => candidate.Parameters.Length == 0);
        if (constructor is null)
            return (CreateKind.None, null, false);

        bool direct = IsAccessible(constructor) && !IsObsolete(constructor) && !HasRequiredMembers(type);
        if (direct)
            return (CreateKind.New, null, true);

        string call = AccessorCall(type, "Construct", genericClasses, out string owner, out string? genericClass, out string? parameters);
        accessors.Add(new AccessorModel(
            "[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]\n" +
            $"public static extern {owner} Construct();",
            genericClass,
            parameters));

        return (CreateKind.Accessor, call + "()", true);
    }

    /// <summary>The description of one member, and the expressions that read and assign it.</summary>
    private MemberModel Member(
        INamedTypeSymbol type,
        Candidate candidate,
        int index,
        List<AccessorModel> accessors,
        Dictionary<INamedTypeSymbol, string> genericClasses)
    {
        bool isStruct = type.IsValueType;
        bool declaredHere = SymbolEqualityComparer.Default.Equals(candidate.Declaring, type);
        string ownerExpression = isStruct || declaredHere ? "value" : $"(({TypeNames.Annotated(candidate.Declaring)})value)";
        string name = Escape(candidate.Name);

        string get;
        string set;

        switch (candidate.Symbol)
        {
            case IFieldSymbol field when IsAccessible(field) && !IsObsolete(field):
                get = $"{ownerExpression}.{name}";
                set = $"{ownerExpression}.{name} = {{0}};";
                break;

            case IFieldSymbol field:
            {
                string call = AccessorCall(candidate.Declaring, $"Field{index}", genericClasses, out string owner, out string? genericClass, out string? parameters);
                string fieldType = TypeNames.Annotated(field.OriginalDefinition.Type);
                accessors.Add(new AccessorModel(
                    $"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"{field.MetadataName}\")]\n" +
                    $"public static extern ref {fieldType} Field{index}({OwnerParameter(owner, isStruct)} owner);",
                    genericClass,
                    parameters));
                get = $"{call}({ReadOwner(isStruct)})";
                set = $"{call}({WriteOwner(isStruct)}) = {{0}};";
                break;
            }

            case IPropertySymbol property:
            {
                if (IsAccessible(property.GetMethod!) && !IsObsolete(property) && !IsObsolete(property.GetMethod!))
                {
                    get = $"{ownerExpression}.{name}";
                }
                else
                {
                    string call = AccessorCall(candidate.Declaring, $"Get{index}", genericClasses, out string owner, out string? genericClass, out string? parameters);
                    accessors.Add(new AccessorModel(
                        $"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"{property.GetMethod!.MetadataName}\")]\n" +
                        $"public static extern {TypeNames.Annotated(property.OriginalDefinition.Type)} Get{index}({OwnerParameter(owner, isStruct)} owner);",
                        genericClass,
                        parameters));
                    get = $"{call}({ReadOwner(isStruct)})";
                }

                var setter = property.SetMethod!;
                if (IsAccessible(setter) && !setter.IsInitOnly && !IsObsolete(property) && !IsObsolete(setter))
                {
                    set = $"{ownerExpression}.{name} = {{0}};";
                }
                else
                {
                    string call = AccessorCall(candidate.Declaring, $"Set{index}", genericClasses, out string owner, out string? genericClass, out string? parameters);
                    accessors.Add(new AccessorModel(
                        $"[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"{setter.MetadataName}\")]\n" +
                        $"public static extern void Set{index}({OwnerParameter(owner, isStruct)} owner, {TypeNames.Annotated(property.OriginalDefinition.Type)} value);",
                        genericClass,
                        parameters));
                    set = $"{call}({WriteOwner(isStruct)}, {{0}});";
                }

                break;
            }

            default:
                throw new System.InvalidOperationException($"Unexpected member '{candidate.Name}'.");
        }

        return new MemberModel(
            candidate.Name,
            TypeNames.Annotated(candidate.Type),
            TypeNames.Plain(candidate.Type),
            candidate.Key,
            get,
            set);
    }

    /// <summary>
    /// How generated code names an accessor of a member declared on <paramref name="declaring"/>: on the
    /// contract itself, or — when the declaring type is generic, which an accessor must be declared
    /// over — on a generic class of its own, called with the declaring type's arguments.
    /// </summary>
    private static string AccessorCall(
        INamedTypeSymbol declaring,
        string method,
        Dictionary<INamedTypeSymbol, string> genericClasses,
        out string owner,
        out string? genericClass,
        out string? parameters)
    {
        owner = Owner(declaring, genericClasses, out genericClass, out parameters);
        if (genericClass is null)
            return method;

        var arguments = AllTypeArguments(declaring).Select(TypeNames.Annotated);
        return $"{genericClass}<{string.Join(", ", arguments)}>.{method}";
    }

    private static string Owner(
        INamedTypeSymbol declaring,
        Dictionary<INamedTypeSymbol, string> genericClasses,
        out string? genericClass,
        out string? parameters)
    {
        if (!IsGeneric(declaring))
        {
            genericClass = null;
            parameters = null;
            return TypeNames.Annotated(declaring);
        }

        var definition = declaring.OriginalDefinition;
        if (!genericClasses.TryGetValue(definition, out genericClass))
        {
            genericClass = $"Access{genericClasses.Count}";
            genericClasses.Add(definition, genericClass);
        }

        parameters = string.Join(", ", TypeNames.AllTypeParameters(definition));
        return TypeNames.Annotated(definition);
    }

    private static bool IsGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
                return true;
        }

        return false;
    }

    private static IEnumerable<ITypeSymbol> AllTypeArguments(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
            chain.Insert(0, current);

        return chain.SelectMany(level => level.TypeArguments);
    }

    private static string OwnerParameter(string owner, bool isStruct) => isStruct ? $"ref {owner}" : owner;

    private static string ReadOwner(bool isStruct) =>
        isStruct ? "ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in value)" : "value";

    private static string WriteOwner(bool isStruct) => isStruct ? "ref value" : "value";

    /// <summary>Gives every contract a class name: its type's name made an identifier, numbered when two collide.</summary>
    private IEnumerable<ContractModel> NameContracts()
    {
        var used = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (_, contract) in _contracts.OrderBy(entry => entry.Contract.TypeOfName, System.StringComparer.Ordinal))
        {
            string stem = TypeNames.Identifier(contract.TypeOfName.Replace("global::", string.Empty)) + "Contract";
            string name = stem;
            for (int suffix = 2; !used.Add(name); suffix++)
                name = stem + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);

            yield return contract with { ClassName = name };
        }
    }

    // --- symbols --------------------------------------------------------------------------------------

    private bool IsAccessible(ISymbol symbol) => _compilation.IsSymbolAccessibleWithin(symbol, _context);

    private bool IsAccessible(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => IsAccessible(array.ElementType),
            INamedTypeSymbol named => _compilation.IsSymbolAccessibleWithin(named, _context) &&
                                      named.TypeArguments.All(IsAccessible),
            _ => _compilation.IsSymbolAccessibleWithin(type, _context)
        };

    private bool IsObsolete(ISymbol symbol) => Find(symbol, _obsolete) is not null;

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var level = type; level is not null; level = level.BaseType)
        {
            if (level.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
                return true;
        }

        return false;
    }

    private static bool ContainsTypeParameters(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameters(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameters) ||
                                      (named.ContainingType is { } container && ContainsTypeParameters(container)),
            _ => false
        };

    private static IMethodSymbol BaseDefinition(IMethodSymbol method)
    {
        var current = method.OriginalDefinition;
        while (current.OverriddenMethod is { } overridden)
            current = overridden.OriginalDefinition;

        return current;
    }

    private static int? IntArgument(AttributeData? attribute) =>
        attribute is { ConstructorArguments.Length: 1 } && attribute.ConstructorArguments[0].Value is int value ? value : null;

    private static AttributeData? Find(ISymbol symbol, INamedTypeSymbol? attribute) =>
        attribute is null ? null : symbol.GetAttributes().FirstOrDefault(data => Is(data, attribute));

    /// <summary>An attribute on a property or, failing that, on the property it overrides, nearest first.</summary>
    private static AttributeData? FindOnProperty(IPropertySymbol property, INamedTypeSymbol? attribute)
    {
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            if (Find(current, attribute) is { } found)
                return found;
        }

        return null;
    }

    private static bool Is(AttributeData data, INamedTypeSymbol? attribute) =>
        attribute is not null && SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute);

    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static string Names(IEnumerable<Candidate> candidates) => string.Join(", ", candidates.Select(candidate => candidate.Name));

    private void Report(DiagnosticDescriptor descriptor, Location? location, params string[] arguments) =>
        _diagnostics.Add(new DiagnosticInfo(descriptor.Id, LocationInfo.From(location), new EquatableArray<string>(arguments)));
}
