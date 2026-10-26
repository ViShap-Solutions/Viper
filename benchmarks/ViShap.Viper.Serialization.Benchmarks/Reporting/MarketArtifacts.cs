using System.Globalization;
using System.Reflection;
using System.Text;
using ViShap.Viper.Serialization.Benchmarks.Adapters;
using ViShap.Viper.Serialization.Benchmarks.Adapters.Market;
using ViShap.Viper.Serialization.Benchmarks.DataSets;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// The files a Track B run writes beside the verification's, so that its report is a view over its own
/// directory and nothing else (REP-02, REP-11): the roster with what was not measured and why, the
/// datasets with their tiers and the model types they reach, and the configuration rationales verbatim.
/// </summary>
internal static class MarketArtifacts
{
    internal const string Roster = "roster.csv";

    internal const string Datasets = "market-datasets.csv";

    internal const string Configuration = "configuration.md";

    private const string ModelNamespace = "ViShap.Viper.Serialization.Benchmarks.Models";

    internal static void Write(string directory)
    {
        Directory.CreateDirectory(directory);

        var roster = new StringBuilder("kind,library,version,configuration,family,intended,compressed,buffer_writer,span,reason\n");

        foreach (var adapter in MarketRoster.All)
        {
            roster.Append(CultureInfo.InvariantCulture,
                $"measured,{Csv.Quote(adapter.Library)},{Csv.Quote(adapter.Version)},{Csv.Quote(adapter.Configuration)},{adapter.Family},");
            roster.Append(CultureInfo.InvariantCulture,
                $"{Csv.Quote(Tiers(adapter.Intended))},{adapter.Compressed},{adapter is IBufferWriterSerializer},{adapter is ISpanDeserializer},\"\"\n");
        }

        foreach (var library in MarketExclusions.SecondCircle)
        {
            roster.Append(CultureInfo.InvariantCulture,
                $"second-circle,{Csv.Quote(library.Library)},{Csv.Quote(library.Version)},\"\",\"\",\"\",False,False,False,");
            roster.Append(Csv.Quote($"{library.Reason}. {MarketExclusions.SecondCircleNotRun}")).Append('\n');
        }

        foreach (var library in MarketExclusions.Excluded)
        {
            roster.Append(CultureInfo.InvariantCulture,
                $"excluded,{Csv.Quote(library.Library)},{Csv.Quote(library.Version)},\"\",\"\",\"\",False,False,False,{Csv.Quote(library.Reason)}\n");
        }

        File.WriteAllText(Path.Combine(directory, Roster), roster.ToString(), Encoding.UTF8);

        var core = Corpus.Core.Select(dataset => dataset.Id).ToHashSet(StringComparer.Ordinal);
        var datasets = new StringBuilder("dataset,name,tiers,core,types\n");

        foreach (var dataset in Corpus.All)
        {
            datasets.Append(CultureInfo.InvariantCulture,
                $"{Csv.Quote(dataset.Id)},{Csv.Quote(dataset.Name)},{Csv.Quote(Tiers(MarketCorpus.TiersOf(dataset.Id)))},");
            datasets.Append(CultureInfo.InvariantCulture,
                $"{core.Contains(dataset.Id)},{Csv.Quote(string.Join(' ', ModelTypes(dataset.ValueType)))}\n");
        }

        File.WriteAllText(Path.Combine(directory, Datasets), datasets.ToString(), Encoding.UTF8);

        var configuration = new StringBuilder("# Configuration rationale\n\n");
        configuration.Append("Every library configuration of the run, in its own documented best production mode, as its adapter\n");
        configuration.Append("states it (Benchmark-Plan §7.3). Written by the run, not by hand.\n");

        foreach (var adapter in MarketRoster.All)
        {
            var rationale = adapter.Rationale;
            configuration.Append(CultureInfo.InvariantCulture, $"\n## {adapter.Library} {adapter.Version} — {adapter.Configuration}\n\n");
            configuration.Append(CultureInfo.InvariantCulture, $"- **Mode used:** {rationale.Mode}\n");
            configuration.Append(CultureInfo.InvariantCulture, $"- **Options set, and where they come from:** {rationale.Options}\n");
            configuration.Append(CultureInfo.InvariantCulture, $"- **Deliberately not enabled:** {rationale.NotEnabled}\n");
            configuration.Append(CultureInfo.InvariantCulture, $"- **A known faster mode not used:** {rationale.FasterModeNotUsed}\n");
        }

        File.WriteAllText(Path.Combine(directory, Configuration), configuration.ToString(), Encoding.UTF8);
    }

    /// <summary>Tier flags as the report prints them: <c>T0 T2</c>.</summary>
    internal static string Tiers(Tier tiers) =>
        string.Join(' ', Enum.GetValues<Tier>().Where(tier => tier != Tier.None && (tiers & tier) != 0));

    /// <summary>
    /// The model types a dataset's value reaches, by simple name, so a structural difference of FAIR-20
    /// can be printed beside every dataset it touches.
    /// </summary>
    private static IReadOnlyList<string> ModelTypes(Type root)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);

        while (pending.Count > 0)
        {
            var type = pending.Pop();

            if (!seen.Add(type))
            {
                continue;
            }

            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    pending.Push(argument);
                }
            }

            if (type.Namespace?.StartsWith(ModelNamespace, StringComparison.Ordinal) != true)
            {
                continue;
            }

            int arity = type.Name.IndexOf('`', StringComparison.Ordinal);
            found.Add(arity < 0 ? type.Name : type.Name[..arity]);

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                switch (member)
                {
                    case PropertyInfo property:
                        pending.Push(property.PropertyType);
                        break;

                    case FieldInfo field when !field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)):
                        pending.Push(field.FieldType);
                        break;
                }
            }

            if (type.BaseType is { } baseType)
            {
                pending.Push(baseType);
            }
        }

        return [.. found];
    }
}
