using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.References;

/// <summary>
/// Pins CYC-11: without reference framing a cycle is found by searching the path from the root to
/// the value being written. The refusal is the same at every depth, and an instance that appears
/// more than once without being its own ancestor is written again rather than mistaken for a cycle.
/// </summary>
public class CycleDetectionTests
{
    private const string Diagnostic = "Circular reference detected while serializing";

    private readonly BinarySerializer _serializer = new();

    /// <summary>A chain of <paramref name="length"/> nodes; the first is returned, the last through <paramref name="last"/>.</summary>
    private static Cyclic Chain(int length, out Cyclic last)
    {
        var first = new Cyclic { Name = "0" };
        last = first;

        for (int index = 1; index < length; index++)
        {
            var next = new Cyclic { Name = index.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            last.Next = next;
            last = next;
        }

        return first;
    }

    [Fact]
    public void Serialize_ACycleAtDepthOne_ThrowsType()
    {
        var root = new Cyclic { Name = "self" };
        root.Next = root;

        AssertEx.Throws<BinaryTypeException>(Diagnostic, () => _serializer.Serialize(root));
    }

    [Fact]
    public void Serialize_ACycleAtDepthFiveHundred_ThrowsTheSameType()
    {
        var root = Chain(500, out var last);
        last.Next = root;

        var deep = AssertEx.Throws<BinaryTypeException>(Diagnostic, () => _serializer.Serialize(root));

        var self = new Cyclic { Name = "self" };
        self.Next = self;
        var shallow = Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(self));
        Assert.Equal(shallow.Message, deep.Message);
    }

    [Fact]
    public void Serialize_ACycleClosingDeepBelowTheRoot_ThrowsType()
    {
        var root = Chain(500, out var last);
        var middle = root;
        for (int index = 0; index < 250; index++)
            middle = middle.Next!;

        last.Next = middle;

        AssertEx.Throws<BinaryTypeException>(Diagnostic, () => _serializer.Serialize(root));
    }

    [Fact]
    public void Serialize_ADeepAcyclicGraphRepeatingAnInstance_IsNotRefused()
    {
        var shared = Chain(200, out _);
        var graph = new List<Cyclic>();
        for (int branch = 0; branch < 3; branch++)
        {
            var head = Chain(250, out var tail);
            tail.Next = shared;
            graph.Add(head);
        }

        byte[] payload = _serializer.Serialize(graph);
        var restored = _serializer.Deserialize<List<Cyclic>>(payload)!;

        Assert.Equal(3, restored.Count);
        Assert.NotSame(restored[0], restored[1]);
    }

    [Fact]
    public void Serialize_TheSameInstanceAsSiblings_IsNotRefused()
    {
        var shared = new Cyclic { Name = "shared" };

        byte[] payload = _serializer.Serialize(new List<Cyclic> { shared, shared, shared });

        Assert.Equal(3, _serializer.Deserialize<List<Cyclic>>(payload)!.Count);
    }

    [Fact]
    public void Serialize_AfterACycleWasRefused_TheNextCallSucceeds()
    {
        var root = new Cyclic { Name = "self" };
        root.Next = root;
        Assert.Throws<BinaryTypeException>(() => _serializer.Serialize(root));

        var value = Chain(10, out _);

        Assert.Equal("0", _serializer.Deserialize<Cyclic>(_serializer.Serialize(value))!.Name);
    }
}
