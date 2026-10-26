using System.Reflection;
using ViShap.Viper.Serialization.Benchmarks.DataSets;
using ViShap.Viper.Serialization.Benchmarks.Models;
using ViShap.Viper.Serialization.Benchmarks.Verification;

namespace ViShap.Viper.Serialization.Benchmarks.Schema;

/// <summary>
/// The logical schema of the corpus (Benchmark-Plan FAIR-08): the Viper models every dataset is generated
/// in, read by reflection, so the schema cannot drift from the data. Every other library's models are
/// derived from it under the same names, and <see cref="Failures"/> proves they carry the same members of
/// the same types and the same data (FAIR-19); what a library must carry differently is recorded in
/// <see cref="ModelFamilies.Differences"/> (FAIR-20).
/// </summary>
internal static class ModelSchema
{
    /// <summary>
    /// FAIR-19: each family carries every schema type under its name, with the same members of the same
    /// types, and the projection of every dataset equals its source member by member.
    /// </summary>
    internal static IReadOnlyList<string> Failures()
    {
        var failures = new List<string>();
        var schemaTypes = Types();

        foreach (var family in ModelFamilies.Competitors)
        {
            foreach (var type in schemaTypes)
            {
                var variant = typeof(ModelFamilies).Assembly.GetType($"{ModelFamilies.Namespace(family)}.{type.Name}");
                if (variant is null)
                {
                    failures.Add($"{family}: no type {type.Name}");
                    continue;
                }

                if (type.IsEnum)
                {
                    if (!Enum.GetNames(type).SequenceEqual(Enum.GetNames(variant))
                        || Enum.GetUnderlyingType(type) != Enum.GetUnderlyingType(variant))
                        failures.Add($"{family}: enum {type.Name} differs in its names or its underlying type");

                    continue;
                }

                if (type.BaseType is { } baseType && ModelFamilies.IsModel(baseType) && variant.BaseType?.Name != baseType.Name)
                    failures.Add($"{family}: {type.Name} derives from {variant.BaseType?.Name} instead of {baseType.Name}");

                var expected = Members(type).ToDictionary(member => member.Name, member => ModelFamilies.Map(member.PropertyType, family));
                var actual = Members(variant).ToDictionary(member => member.Name, member => member.PropertyType);

                foreach (var (name, memberType) in expected)
                {
                    if (!actual.TryGetValue(name, out var variantType))
                        failures.Add($"{family}: {type.Name}.{name} is missing");
                    else if (variantType != memberType)
                        failures.Add($"{family}: {type.Name}.{name} is {variantType.Name}, the schema says {memberType.Name}");
                }

                foreach (var name in actual.Keys.Except(expected.Keys))
                    failures.Add($"{family}: {type.Name}.{name} is not in the schema");
            }

            foreach (var dataset in MarketCorpus.For(family))
            {
                var source = Corpus.Find(dataset.Id);
                if (!StructuralComparer.EqualAcrossFamilies(source.BoxedValue, dataset.BoxedValue, out var difference))
                    failures.Add($"{family}: the projection of {dataset.Id} differs from its source: {difference}");
            }
        }

        return failures;
    }

    /// <summary>The Viper models the corpus reaches, which are the schema of every family.</summary>
    internal static IReadOnlyList<Type> Types()
    {
        var found = new HashSet<Type>();
        var pending = new Stack<Type>(Corpus.All.Select(dataset => dataset.ValueType));

        // DATA-13's reading side is part of the schema even though no dataset's root is its type.
        pending.Push(typeof(Models.Viper.KeyedOrderV2));

        while (pending.TryPop(out var type))
        {
            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    pending.Push(argument);

                continue;
            }

            if (!ModelFamilies.IsModel(type) || !found.Add(type))
                continue;

            if (type.BaseType is { } baseType)
                pending.Push(baseType);

            foreach (var union in type.GetCustomAttributes<BinaryUnionAttribute>())
                pending.Push(union.DerivedType);

            foreach (var member in Members(type))
                pending.Push(member.PropertyType);
        }

        return [.. found.OrderBy(type => type.Name, StringComparer.Ordinal)];
    }

    private static IEnumerable<PropertyInfo> Members(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0);
}
