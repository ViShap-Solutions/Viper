namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// The contracts the source generator writes for the shapes of the conformance suite; the suite runs
/// against them through <c>GeneratedContractConformanceTests</c>.
/// </summary>
[BinaryContext(
    typeof(ConformancePositional), typeof(ConformanceKeyed), typeof(ConformanceDerived),
    typeof(ConformanceKeyedBase), typeof(ConformanceKeyedDerived), typeof(ConformanceShadowed),
    typeof(ConformanceOverridden), typeof(ConformanceShape), typeof(ConformancePoint),
    typeof(ConformanceKeyedPoint))]
public partial class GeneratedConformanceContracts : BinarySerializerContext;

/// <summary>
/// The contracts the source generator writes for the frozen shapes of <c>Fixtures/Wire</c>, through
/// which the frozen payloads are read and written again.
/// </summary>
[BinaryContext(
    typeof(FrozenPrimitives), typeof(FrozenTimeAndSystem), typeof(FrozenNumerics), typeof(FrozenCollections),
    typeof(FrozenComposites), typeof(FrozenKeyed), typeof(FrozenShape), typeof(FrozenGraph),
    typeof(FrozenAbsences))]
public partial class GeneratedCompatibilityContracts : BinarySerializerContext;
