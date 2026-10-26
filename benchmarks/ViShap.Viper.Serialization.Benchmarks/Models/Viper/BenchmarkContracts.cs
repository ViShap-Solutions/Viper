namespace ViShap.Viper.Serialization.Benchmarks.Models.Viper;

/// <summary>
/// The generated contracts of the generator profiles, B-P0g and B-P1g. It lists the root of every
/// dataset and every model a suite serializes on its own; the generator reaches the rest through
/// members, elements and union arms, as a consumer's context does.
/// </summary>
/// <remarks>
/// The profiles that use it also call <c>RequireGeneratedContracts()</c>, so a type the context does not
/// hold is refused rather than quietly described by reflection, and a generated cell can never be a
/// reflected one under another name.
/// </remarks>
[BinaryContext(
    typeof(TinyFlat),
    typeof(MediumObject),
    typeof(ScalarRecord),
    typeof(DeepNode),
    typeof(DagNode),
    typeof(CyclicNode),
    typeof(EventBase),
    typeof(KeyedOrder),
    typeof(KeyedOrderV2),
    typeof(BlobEnvelope),
    typeof(NumericArrays),
    typeof(NullSparse),
    typeof(WideObject),
    typeof(CollectionZoo),
    typeof(TimeAndNumerics),
    typeof(WidePositional005),
    typeof(WidePositional020),
    typeof(WidePositional050),
    typeof(WidePositional100),
    typeof(WideKeyed005),
    typeof(WideKeyed020),
    typeof(WideKeyed050),
    typeof(WideKeyed100),
    typeof(WideKeyed200),
    typeof(FirstUseWarmup))]
internal sealed partial class BenchmarkContracts : BinarySerializerContext;

/// <summary>
/// The type a first-use child serializes before it measures anything, so the path every type shares is
/// already compiled and what a row shows is the type's own first use.
/// </summary>
internal sealed class FirstUseWarmup
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<int> Values { get; set; } = [];
}
