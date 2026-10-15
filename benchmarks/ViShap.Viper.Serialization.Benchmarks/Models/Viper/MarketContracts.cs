namespace ViShap.Viper.Serialization.Benchmarks.Models.Viper;

/// <summary>
/// The generated context Viper is compared through in Track B, its documented best mode: every root the
/// corpus and the probes serialize, so each call takes the root's type information and nothing is
/// described by reflection. Kept apart from <see cref="BenchmarkContracts"/>, which the Track A
/// generator profiles measure, so neither track's cells move when the other's context changes.
/// </summary>
[BinaryContext(
    typeof(TinyFlat),
    typeof(MediumObject),
    typeof(List<TinyFlat>),
    typeof(Dictionary<string, ScalarRecord>),
    typeof(DeepNode),
    typeof(List<string>),
    typeof(BlobEnvelope),
    typeof(DagNode),
    typeof(CyclicNode),
    typeof(List<EventBase>),
    typeof(List<KeyedOrder>),
    typeof(List<KeyedOrderV2>),
    typeof(List<BlobEnvelope>),
    typeof(NumericArrays),
    typeof(List<NullSparse>),
    typeof(WideObject),
    typeof(CollectionZoo),
    typeof(List<TimeAndNumerics>),
    typeof(ProbeRecord),
    typeof(ProbeRecordAdded),
    typeof(ProbeRecordRemoved),
    typeof(ProbeRecordReordered),
    typeof(ProbePair),
    typeof(ProbeCycle),
    typeof(List<ProbeShape>),
    typeof(ProbeDeep))]
internal sealed partial class MarketContracts : BinarySerializerContext;
