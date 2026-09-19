using System.Collections.ObjectModel;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

/// <summary>
/// Pins RT-C05: across an interface-typed member, contract §23 promises reference identity and
/// explicitly does not promise the concrete type. The two halves are asserted separately, because a
/// suite that only compared contents would pass while either one was broken.
/// </summary>
public class InterfaceMemberTests
{
    private static readonly BinarySerializer Referencing = new(
        BinarySerializerOptions.Configure().PreserveReferences().Build());

    private static readonly BinarySerializer Plain = new();

    [Fact]
    public void Deserialize_OneInstanceBehindTwoInterfaceMembers_RestoresOneInstance()
    {
        var shared = new List<int> { 1, 2, 3 };
        var source = new SharedInterfaceLists { A = shared, B = shared };

        var restored = Referencing.Deserialize<SharedInterfaceLists>(Referencing.Serialize(source))!;

        Assert.Same(restored.A, restored.B);
        Assert.Equal([1, 2, 3], restored.A!);
    }

    [Fact]
    public void Deserialize_OneInstanceBehindTwoInterfaceMembersWithoutPreserveReferences_RestoresTwo()
    {
        var shared = new List<int> { 1, 2, 3 };
        var source = new SharedInterfaceLists { A = shared, B = shared };

        var restored = Plain.Deserialize<SharedInterfaceLists>(Plain.Serialize(source))!;

        Assert.NotSame(restored.A, restored.B);
        Assert.Equal(restored.A, restored.B);
    }

    [Fact]
    public void Deserialize_InterfaceMember_ResolvesToTheDocumentedTypeAndNotTheWrittenOne()
    {
        var source = new InterfaceMember
        {
            Items = new ObservableCollection<int> { 1, 2 },
            Map = new SortedDictionary<string, int> { ["a"] = 1 }
        };

        var restored = Referencing.Deserialize<InterfaceMember>(Referencing.Serialize(source))!;

        Assert.Equal(typeof(List<int>), restored.Items!.GetType());
        Assert.Equal(typeof(Dictionary<string, int>), restored.Map!.GetType());
        Assert.Equal([1, 2], restored.Items);
        Assert.Equal(1, restored.Map["a"]);
    }
}
