using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ViShap.Viper.Generator;

/// <summary>
/// Writes the type contracts of every <c>partial</c> class marked <c>[BinaryContext]</c>: one contract per
/// listed type and per member-encoded type it reaches, each describing its type exactly as the serializer
/// would by reflection, and a constructor that adds them to the context. A project with no
/// <c>[BinaryContext]</c> class gets nothing.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ContractGenerator : IIncrementalGenerator
{
    private const string ContextAttribute = "ViShap.Viper.BinaryContextAttribute";

    /// <summary>The build property that says whether the application keeps the reflection path.</summary>
    private const string ReflectionProperty = "build_property.ViperReflectionEnabled";

    /// <summary>The tracking name of the step that plans each context, for incremental tests.</summary>
    internal const string PlanStep = "Plan";

    /// <summary>The tracking name of the step that yields each contract, for incremental tests.</summary>
    internal const string ContractStep = "Contract";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(ContextAttribute, static (node, _) => node is ClassDeclarationSyntax, Plan)
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName(PlanStep);

        var reflectionOff = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue(ReflectionProperty, out string? value) &&
            string.Equals(value?.Trim(), "false", System.StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(models.Combine(reflectionOff), static (output, item) =>
        {
            foreach (var diagnostic in item.Left.Diagnostics)
            {
                var reported = item.Right && diagnostic.Id == Descriptors.DescribedByReflection.Id
                    ? diagnostic with { Id = Descriptors.UnavailableWithoutReflection.Id }
                    : diagnostic;

                output.ReportDiagnostic(reported.ToDiagnostic());
            }
        });

        var headers = models
            .Where(static model => model.CanEmit)
            .Select(static (model, _) => (model.Header, Body: new ContextBody(
                new EquatableArray<string>(model.Contracts.Select(contract => contract.ClassName)),
                model.Supported,
                model.TypeInfos)));

        context.RegisterSourceOutput(headers, static (output, item) =>
            output.AddSource(Emitter.ContextHint(item.Header), Emitter.Context(item.Header, item.Body)));

        var contracts = models
            .Where(static model => model.CanEmit)
            .SelectMany(static (model, _) => model.Contracts.Select(contract => (model.Header, Contract: contract)))
            .WithTrackingName(ContractStep);

        context.RegisterSourceOutput(contracts, static (output, item) =>
            output.AddSource(Emitter.ContractHint(item.Header, item.Contract), Emitter.Contract(item.Header, item.Contract)));
    }

    /// <summary>
    /// The model of the class the attribute is on. A class whose <c>[BinaryContext]</c> attributes are
    /// spread over several partial declarations is planned once, from the declaration holding the first
    /// of them, with all of them.
    /// </summary>
    private static ContextModel? Plan(GeneratorAttributeSyntaxContext source, CancellationToken cancellation)
    {
        if (source.TargetSymbol is not INamedTypeSymbol context)
            return null;

        var attributes = context.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == ContextAttribute)
            .ToList();

        var first = attributes
            .Select(attribute => attribute.ApplicationSyntaxReference)
            .Where(reference => reference is not null)
            .OrderBy(reference => reference!.SyntaxTree.FilePath, System.StringComparer.Ordinal)
            .ThenBy(reference => reference!.Span.Start)
            .FirstOrDefault();

        if (first is null || first.SyntaxTree != source.TargetNode.SyntaxTree || !source.TargetNode.Span.Contains(first.Span))
            return null;

        return Planner.Plan(context, attributes, source.SemanticModel.Compilation, cancellation);
    }
}
