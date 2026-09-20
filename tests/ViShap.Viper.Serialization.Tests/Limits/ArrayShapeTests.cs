using ViShap.Viper.Security;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Limits;

/// <summary>
/// Pins LIM-10…LIM-14: a multidimensional shape is validated as a whole before a single element is
/// read — every dimension, the overflow-safe product, and the element budget the product implies.
/// </summary>
public class ArrayShapeTests
{
    private static BinarySerializer Limited(SerializationLimits limits) =>
        new(BinarySerializerOptions.Configure().WithLimits(limits).Build());

    // --- LIM-10: a dimension below zero ---------------------------------------------------------

    [Fact]
    public void Deserialize_NegativeDimension_ThrowsFormat()
    {
        byte[] frame = Wire.MultiDimensionalArray([2, -1], int32Elements: 0);

        var ex = Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int[,]>(frame));

        Assert.IsNotType<BinaryLimitException>(ex);
    }

    [Fact]
    public void Deserialize_NegativeDimension_RejectsBeforeCreatingTheArray()
    {
        byte[] frame = Wire.MultiDimensionalArray([int.MaxValue, -1], int32Elements: 0);

        AssertEx.AllocatesLessThan(
            1024 * 1024, () => new BinarySerializer().Deserialize<int[,]>(frame));
    }

    // --- LIM-11: a zero dimension ---------------------------------------------------------------

    [Fact]
    public void Deserialize_ZeroDimension_YieldsAnEmptyArrayOfTheDeclaredShape()
    {
        byte[] frame = Wire.MultiDimensionalArray([0, 5], int32Elements: 0);

        var restored = new BinarySerializer().Deserialize<int[,]>(frame);

        Assert.Equal(0, restored!.Length);
        Assert.Equal(0, restored.GetLength(0));
        Assert.Equal(5, restored.GetLength(1));
    }

    [Fact]
    public void Deserialize_ZeroDimensionBesideAnOversizedOne_ThrowsLimit()
    {
        // The product collapses to zero, but the shape still describes an array the runtime cannot
        // create, so each dimension is bounded on its own (§5.2).
        byte[] frame = Wire.MultiDimensionalArray([0, int.MaxValue], int32Elements: 0);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<int[,]>(frame));
    }

    [Fact]
    public void Deserialize_ZeroDimensionBesideADimensionAtTheLimit_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 1_000 });
        byte[] frame = Wire.MultiDimensionalArray([0, 1_000], int32Elements: 0);

        var restored = serializer.Deserialize<int[,]>(frame);

        Assert.Equal(0, restored!.Length);
        Assert.Equal(1_000, restored.GetLength(1));
    }

    [Fact]
    public void Deserialize_DimensionOneAboveMaxArrayLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 1_000 });
        byte[] frame = Wire.MultiDimensionalArray([0, 1_001], int32Elements: 0);

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int[,]>(frame));
    }

    [Fact]
    public void Serialize_Deserialize_ZeroDimension_RoundTrips()
    {
        var serializer = new BinarySerializer();
        var original = new int[0, 5];

        var restored = serializer.Deserialize<int[,]>(serializer.Serialize(original));

        Assert.Equal(0, restored!.GetLength(0));
        Assert.Equal(5, restored.GetLength(1));
    }

    // --- LIM-12 / LIM-13: the product against MaxArrayLength -------------------------------------

    [Fact]
    public void Deserialize_ProductBelowMaxArrayLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 6 });
        byte[] frame = Wire.MultiDimensionalArray([2, 2], int32Elements: 4);

        Assert.Equal(4, serializer.Deserialize<int[,]>(frame)!.Length);
    }

    [Fact]
    public void Deserialize_ProductExactlyAtMaxArrayLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 6 });
        byte[] frame = Wire.MultiDimensionalArray([2, 3], int32Elements: 6);

        var restored = serializer.Deserialize<int[,]>(frame);

        Assert.Equal(6, restored!.Length);
        Assert.Equal(2, restored.GetLength(0));
        Assert.Equal(3, restored.GetLength(1));
    }

    [Fact]
    public void Deserialize_ProductOneAboveMaxArrayLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 6 });
        byte[] frame = Wire.MultiDimensionalArray([7, 1], int32Elements: 0);

        Assert.Throws<BinaryLimitException>(() => serializer.Deserialize<int[,]>(frame));
    }

    [Fact]
    public void Serialize_ProductOneAboveMaxArrayLength_ThrowsLimit()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 6 });

        Assert.Throws<BinaryLimitException>(() => serializer.Serialize(new int[7, 1]));
    }

    [Fact]
    public void Serialize_ProductExactlyAtMaxArrayLength_Succeeds()
    {
        var serializer = Limited(SerializationLimits.Default with { MaxArrayLength = 6 });

        Assert.NotEmpty(serializer.Serialize(new int[2, 3]));
    }

    // --- LIM-14: a product that would overflow ---------------------------------------------------

    [Fact]
    public void Deserialize_ProductOverflowingInt64_ThrowsLimit()
    {
        // Three maximal dimensions multiply out past Int64, so the check has to refuse the shape
        // rather than compute it.
        byte[] frame = Wire.MultiDimensionalArray(
            [int.MaxValue, int.MaxValue, int.MaxValue], int32Elements: 0);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<int[,,]>(frame));
    }

    [Fact]
    public void Deserialize_ProductOverflowingInt64_RejectsBeforeAllocating()
    {
        byte[] frame = Wire.MultiDimensionalArray(
            [int.MaxValue, int.MaxValue, int.MaxValue], int32Elements: 0);

        AssertEx.AllocatesLessThan(
            1024 * 1024, () => new BinarySerializer().Deserialize<int[,,]>(frame));
    }

    [Fact]
    public void Deserialize_ProductThatWouldWrapToASmallPositive_ThrowsLimit()
    {
        // 65536 x 65536 wraps to zero in 32-bit arithmetic; in 64-bit it is far past the limit.
        byte[] frame = Wire.MultiDimensionalArray([65_536, 65_536], int32Elements: 0);

        Assert.Throws<BinaryLimitException>(
            () => new BinarySerializer().Deserialize<int[,]>(frame));
    }

    // --- The declared rank must match the target -------------------------------------------------

    [Fact]
    public void Deserialize_RankDisagreeingWithTheDeclaredType_ThrowsFormat()
    {
        byte[] frame = Wire.MultiDimensionalArray([2, 2], int32Elements: 4, declaredRank: 3);

        Assert.Throws<BinaryFormatException>(
            () => new BinarySerializer().Deserialize<int[,]>(frame));
    }
}
