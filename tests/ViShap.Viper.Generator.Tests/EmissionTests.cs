using ViShap.Viper.Generator.Tests.Fixtures;

namespace ViShap.Viper.Generator.Tests;

/// <summary>
/// Pins GEN-01: the source the generator writes for each object shape — positional, keyed, inherited,
/// shadowed, overridden, union, struct, nested, generic, and the members only an accessor reaches — as a
/// reviewed snapshot, and that the source compiles together with the types it describes.
/// </summary>
public class EmissionTests
{
    private const string Usings = "using System.Collections.Generic;\nusing ViShap.Viper;\n";

    [Fact]
    public void Positional_OrderNameAndInclusion()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public class Account
            {
                [BinaryInclude] private int _revision;
                [BinaryOrder(1)] public int Id { get; set; }
                public string? Owner { get; set; }
                public decimal Balance;
                [BinaryIgnore] public string? DisplayName { get; set; }
                public int View => Id;
            }

            [BinaryContext(typeof(Account))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Positional", run.Source("Shapes_AccountContract.g.cs"));
        Snapshot.Match("Context", run.Source("Shapes_Contracts.g.cs"));
    }

    [Fact]
    public void Keyed_KeysInAscendingOrder()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            [BinaryContract]
            public class Customer
            {
                [BinaryKey(3)] public List<string>? Tags { get; set; }
                [BinaryKey(1)] public string Name { get; set; } = "";
                [BinaryKey(200)] private long _big;
                [BinaryIgnore] public int Cached { get; set; }
            }

            [BinaryContext(typeof(Customer))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Keyed", run.Source("Shapes_CustomerContract.g.cs"));
    }

    [Fact]
    public void Inherited_BaseMembersAndANonPublicOne()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public class Animal
            {
                [BinaryInclude] private int _legs;
                public string? Name { get; set; }
            }

            public class Dog : Animal
            {
                public bool GoodBoy { get; set; }
            }

            [BinaryContract]
            public class KeyedAnimal { [BinaryKey(1)] public int Id { get; set; } }

            public class KeyedDog : KeyedAnimal { [BinaryKey(2)] public int Bones { get; set; } }

            [BinaryContext(typeof(Dog), typeof(KeyedDog))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Inherited", run.Source("Shapes_DogContract.g.cs"));
        Snapshot.Match("InheritedKeyed", run.Source("Shapes_KeyedDogContract.g.cs"));
    }

    [Fact]
    public void Shadowed_BothDeclarationsTheBaseFirst()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public class Plain { public int Value { get; set; } }
            public class Hiding : Plain { public new string? Value { get; set; } }

            [BinaryContext(typeof(Hiding))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Shadowed", run.Source("Shapes_HidingContract.g.cs"));
    }

    [Fact]
    public void Overridden_OneMemberWithTheOverridesAttribute()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public class Virtual
            {
                public virtual int Value { get; set; }
                [BinaryOrder(9)] public virtual int Getter { get; set; }
                public int Other { get; set; }
            }

            public class Overriding : Virtual
            {
                [BinaryOrder(0)] public override int Value { get; set; }
                public override int Getter => 1;
            }

            [BinaryContext(typeof(Overriding))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Overridden", run.Source("Shapes_OverridingContract.g.cs"));
    }

    [Fact]
    public void Union_TheBaseAndEveryArm()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            [BinaryUnion(1, typeof(Circle))]
            [BinaryUnion(2, typeof(Square))]
            public abstract class Shape { public string? Label { get; set; } }
            public sealed class Circle : Shape { public double Radius { get; set; } }
            public sealed class Square : Shape { public int Side { get; set; } }

            [BinaryContext(typeof(Shape))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("UnionBase", run.Source("Shapes_ShapeContract.g.cs"));
        Snapshot.Match("UnionArm", run.Source("Shapes_CircleContract.g.cs"));
        Assert.Contains(run.Sources.Keys, hint => hint.EndsWith("Shapes_SquareContract.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Struct_AssignedInPlaceThroughRef()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public struct Point
            {
                public int X;
                public int Y { get; set; }
                [BinaryInclude] private int _z;
            }

            [BinaryContract]
            public struct KeyedPoint { [BinaryKey(1)] public int X; [BinaryKey(2)] private string? _tag; }

            [BinaryContext(typeof(Point), typeof(KeyedPoint))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Struct", run.Source("Shapes_PointContract.g.cs"));
        Snapshot.Match("StructKeyed", run.Source("Shapes_KeyedPointContract.g.cs"));
    }

    [Fact]
    public void Nested_ContextAndTypesInsideOtherTypes()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public partial class Host
            {
                public class Inner { public int N { get; set; } public Leaf? Child { get; set; } }
                public class Leaf { public string? Text { get; set; } }

                [BinaryContext(typeof(Inner))]
                public partial class Contracts : BinarySerializerContext;
            }
            """);

        AssertCompiles(run);
        Snapshot.Match("NestedContext", run.Source("Shapes_Host_Contracts.g.cs"));
        Snapshot.Match("Nested", run.Source("Shapes_Host_InnerContract.g.cs"));
    }

    [Fact]
    public void Generic_AClosedTypeAndItsNonPublicMembers()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public class Box<T>
            {
                [BinaryInclude] private T _value = default!;
                public List<T>? Items { get; set; }
                public T? Peek() => _value;
            }

            public class Holder { public Box<int>? Numbers { get; set; } public Box<string>? Words { get; set; } }

            [BinaryContext(typeof(Holder))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("GenericInt", run.Source("Shapes_Box_intContract.g.cs"));
        Snapshot.Match("GenericString", run.Source("Shapes_Box_stringContract.g.cs"));
    }

    [Fact]
    public void Accessors_InitRequiredPrivateSetterAndPrivateConstructor()
    {
        var run = Harness.Run(Usings + """
            namespace Shapes;

            public class Guarded
            {
                private Guarded() { }
                public required string Name { get; set; }
                public int Version { get; init; }
                public int Count { get; private set; }
                [BinaryInclude] private string? Secret { get; set; }
                public int @class { get; set; }
            }

            [BinaryContext(typeof(Guarded))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("Accessors", run.Source("Shapes_GuardedContract.g.cs"));
    }

    [Fact]
    public void GlobalNamespace_ContextWithoutANamespace()
    {
        var run = Harness.Run("""
            using ViShap.Viper;

            public class Thing { public int Id { get; set; } }

            [BinaryContext(typeof(Thing))]
            public partial class Contracts : BinarySerializerContext;
            """);

        AssertCompiles(run);
        Snapshot.Match("GlobalNamespace", run.Source("Contracts.ThingContract.g.cs"));
    }

    private static void AssertCompiles(Harness.Generated run)
    {
        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.CompilerErrors);
    }
}
