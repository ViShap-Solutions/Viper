using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins PM-02, PM-07 and PM-11…PM-16: a union is closed and explicit. Several implementations are
/// told apart by one tag byte, a declaration that could not be read back is refused where it is
/// declared, and the only thing that travels is the tag — never a type name a payload could ask to
/// have constructed.
/// </summary>
public class UnionDeclarationTests
{
    private readonly BinarySerializer _serializer = new();

    private readonly BinarySerializer _compact =
        new(BinarySerializerOptions.Configure().WithVersion(0).Build());

    [Fact]
    public void Deserialize_SeveralDerivedTypesUnderOneBase_AreToldApartByTag()
    {
        var source = new List<Shape>
        {
            new Circle { Label = "c", Radius = 1.5 },
            new Square { Label = "s", Side = 2.5 },
            new Triangle { Label = "t", Base = 3.5 }
        };

        var result = _serializer.Deserialize<List<Shape>>(_serializer.Serialize(source))!;

        Assert.Equal(1.5, Assert.IsType<Circle>(result[0]).Radius);
        Assert.Equal(2.5, Assert.IsType<Square>(result[1]).Side);
        Assert.Equal(3.5, Assert.IsType<Triangle>(result[2]).Base);
        Assert.Equal(["c", "s", "t"], result.Select(shape => shape.Label));
    }

    [Fact]
    public void Serialize_SeveralDerivedTypesUnderOneBase_DifferOnlyInTheTagByte()
    {
        // Same layout, different tag: the discriminator is what distinguishes them, not the members.
        byte[] circle = _compact.Serialize<Shape>(new Circle { Label = "x", Radius = 1 });
        byte[] square = _compact.Serialize<Shape>(new Square { Label = "x", Side = 1 });

        Assert.Equal(circle.Length, square.Length);
        Assert.Equal(1, circle[1]);
        Assert.Equal(2, square[1]);
        Assert.Equal(circle[2..], square[2..]);
    }

    [Fact]
    public void Serialize_TaggedValue_PutsExactlyOneTagByteBeforeTheMemberLayout()
    {
        // Null flag, tag, then the members of UnionDerived in plan order: "A" before "Z".
        byte[] expected = Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write((byte)1);
            writer.Write(11);
            writer.Write(22);
        });

        Assert.Equal(
            expected, _compact.Serialize<UnionBase>(new UnionDerived { A = 11, Z = 22 }));
    }

    [Fact]
    public void Deserialize_TagsAtBothEndsOfTheByteRange_RoundTrip()
    {
        var source = new List<TagRange> { new TagZero { Value = 1 }, new TagMax { Value = 2 } };

        byte[] payload = _compact.Serialize(source);
        var result = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithVersion(0).AllowV0Fallback().Build()).Deserialize<List<TagRange>>(payload)!;

        Assert.Equal(1, Assert.IsType<TagZero>(result[0]).Value);
        Assert.Equal(2, Assert.IsType<TagMax>(result[1]).Value);
        Assert.Equal(0, payload[6]);
        Assert.Equal(255, payload[12]);
    }

    [Fact]
    public void Deserialize_UnknownDiscriminator_ThrowsType()
    {
        byte[] payload = Wire.Frame(Wire.Payload(writer =>
        {
            writer.Write(true);
            writer.Write((byte)9);
            writer.Write(0);
        }));

        AssertEx.Throws<BinaryTypeException>(
            "Unknown discriminator", () => _serializer.Deserialize<UnionBase>(payload));
    }

    [Fact]
    public void Serialize_DuplicateUnionTags_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "duplicate [BinaryUnion] tag",
            () => _serializer.Serialize<DuplicateTagBase>(new FirstClaim()));
    }

    [Fact]
    public void Serialize_TagAboveTheByteRange_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "must fit in a byte", () => _serializer.Serialize<AboveRangeBase>(new AboveRange()));
    }

    [Fact]
    public void Serialize_TagBelowTheByteRange_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "must fit in a byte", () => _serializer.Serialize<BelowRangeBase>(new BelowRange()));
    }

    [Fact]
    public void Deserialize_KnownTypeNotAssignableToTheBase_ThrowsType()
    {
        AssertEx.Throws<BinaryTypeException>(
            "is not assignable",
            () => _serializer.Deserialize<UnrelatedKnownTypeBase>(
                Wire.Frame(Wire.Payload(writer => writer.Write(true)))));
    }

    [Fact]
    public void Serialize_UnionValue_WritesNoTypeName()
    {
        byte[] payload = _serializer.Serialize(new List<Shape>
        {
            new Circle { Label = "c", Radius = 1 },
            new Square { Label = "s", Side = 2 }
        });

        AssertEx.DoesNotContainBytes(payload, "Circle"u8);
        AssertEx.DoesNotContainBytes(payload, "Square"u8);
        AssertEx.DoesNotContainBytes(payload, "Shape"u8);
        AssertEx.DoesNotContainBytes(payload, "ViShap"u8);
    }

    [Fact]
    public void Serialize_UnionMapTouchedInParallel_YieldsOneConsistentMap()
    {
        // RacedBase is used by nothing else, so this really is the first resolution of its map.
        byte[][] payloads = new byte[64][];
        RacedBase?[] results = new RacedBase?[64];

        Parallel.For(0, 64, index =>
        {
            byte[] payload = _serializer.Serialize<RacedBase>(
                new RacedDerived { A = index, Z = 7 });

            payloads[index] = payload;
            results[index] = _serializer.Deserialize<RacedBase>(payload);
        });

        for (int index = 0; index < 64; index++)
        {
            Assert.Equal(_serializer.Serialize<RacedBase>(new RacedDerived { A = index, Z = 7 }),
                payloads[index]);
            Assert.Equal(index, Assert.IsType<RacedDerived>(results[index]).A);
            Assert.Equal(7, results[index]!.Z);
        }
    }
}
