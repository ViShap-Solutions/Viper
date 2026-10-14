using System.Reflection;
using ViShap.Viper.Engine;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins NAOT-06 and NAOT-07: a generated context makes supported exactly the types the engine reaches
/// from the types it lists — every member type of every member-encoded type, as the engine's own
/// contracts describe them, every element, key and value type, every union arm — and no scalar; and it
/// exposes the type information of each listed type, bound to itself.
/// </summary>
public class GeneratedSupportedTypesTests
{
    public static TheoryData<string> Contexts() =>
        [nameof(GeneratedConformanceContracts), nameof(GeneratedCompatibilityContracts)];

    // --- NAOT-06: the types a generated context makes supported ---------------------------------------

    [Theory]
    [MemberData(nameof(Contexts))]
    public void GeneratedContext_MakesSupportedExactlyTheTypesTheEngineReaches(string name)
    {
        var context = Context(name);

        var reached = Reach(Roots(context.GetType()));
        var supported = Supported(context);

        Assert.Equal(
            reached.Select(type => type.ToString()).Order(StringComparer.Ordinal),
            supported.Select(type => type.ToString()).Order(StringComparer.Ordinal));
    }

    // --- NAOT-07: the type information of a generated context -----------------------------------------

    [Theory]
    [MemberData(nameof(Contexts))]
    public void GeneratedContext_ExposesTheTypeInformationOfEveryListedType(string name)
    {
        var context = Context(name);
        var roots = Roots(context.GetType());

        var infos = context.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType.IsGenericType &&
                               property.PropertyType.GetGenericTypeDefinition() == typeof(BinaryTypeInfo<>))
            .ToArray();

        Assert.Equal(roots.Select(root => root.Name).Order(StringComparer.Ordinal), infos.Select(info => info.Name).Order(StringComparer.Ordinal));
        Assert.All(infos, info =>
        {
            object value = info.GetValue(context)!;
            Assert.Same(context, value.GetType().GetProperty(nameof(BinaryTypeInfo<int>.Context))!.GetValue(value));
            Assert.Equal(info.PropertyType.GetGenericArguments()[0], value.GetType().GetProperty(nameof(BinaryTypeInfo<int>.Type))!.GetValue(value));
        });
    }

    // --- the engine's reach ---------------------------------------------------------------------------

    private static BinarySerializerContext Context(string name) => name switch
    {
        nameof(GeneratedConformanceContracts) => GeneratedConformanceContracts.Default,
        _ => GeneratedCompatibilityContracts.Default
    };

    private static Type[] Roots(Type context) =>
        context.GetCustomAttributes<BinaryContextAttribute>().SelectMany(attribute => attribute.Types).Distinct().ToArray();

    private static HashSet<Type> Supported(BinarySerializerContext context) =>
        (HashSet<Type>)typeof(BinarySerializerContext)
            .GetField("_supported", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!;

    /// <summary>
    /// Every type the engine reaches from <paramref name="roots"/>, scalars left out: what the rule of
    /// each type closes over, and for a member-encoded type the member types of the contract the engine
    /// builds by reflection and the arms of its union map.
    /// </summary>
    private static HashSet<Type> Reach(IEnumerable<Type> roots)
    {
        var reached = new HashSet<Type>();
        var pending = new Queue<Type>(roots);

        while (pending.TryDequeue(out var type))
        {
            if (!reached.Add(type))
                continue;

            switch (TypeRules.Classify(type))
            {
                case TypeRule.Scalar:
                case TypeRule.Delegate:
                    reached.Remove(type);
                    break;

                case TypeRule.Nullable:
                    pending.Enqueue(Nullable.GetUnderlyingType(type)!);
                    break;

                case TypeRule.Array:
                case TypeRule.MultiDimensionalArray:
                    pending.Enqueue(type.GetElementType()!);
                    break;

                case TypeRule.Definition:
                    foreach (var argument in type.GetGenericArguments())
                        pending.Enqueue(argument);
                    break;

                case TypeRule.Collection:
                    pending.Enqueue(TypeRules.CollectionElement(type)!);
                    break;

                case TypeRule.Object:
                    foreach (var arm in Arms(type))
                        pending.Enqueue(arm);

                    if (!type.IsAbstract && !type.IsInterface)
                        foreach (var member in TypeContractCache.Get(type).Members)
                            pending.Enqueue(member.MemberType);
                    break;
            }
        }

        return reached;
    }

    private static IEnumerable<Type> Arms(Type type) =>
        type.GetCustomAttributes<BinaryUnionAttribute>(inherit: false).Select(attribute => attribute.DerivedType);
}
