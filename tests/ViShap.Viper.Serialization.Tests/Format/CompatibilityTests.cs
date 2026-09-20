using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using ViShap.Viper.Checksum;
using ViShap.Viper.Compression;
using ViShap.Viper.Crypto;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Format;

/// <summary>
/// Pins CMPT-01…CMPT-11: the committed <c>Fixtures/Wire/*.bin</c> files were written by the v1.0.0
/// writer and frozen. Every test here reads one and compares it with a value spelled out in this
/// file, so the bytes are checked against an expectation the code cannot move.
/// </summary>
/// <remarks>
/// This is the only guarantee that a later version still reads what this one wrote. Once v1.0.0 is
/// released the fixtures can never be regenerated — a rebuilt fixture would be whatever the code has
/// become, and would agree with itself no matter what changed. A failure here is a compatibility
/// break in the format or in a frozen shape, never a fixture to refresh.
/// </remarks>
public class CompatibilityTests
{
    /// <summary>The key <c>v1-protected.bin</c> was encrypted with. Frozen with the fixture.</summary>
    private static readonly byte[] ProtectedKey =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    /// <summary>Every fixture this suite reads, so one missing file fails loudly rather than quietly.</summary>
    private static readonly string[] Fixtures =
    [
        "v1-primitives.bin", "v1-time-system.bin", "v1-numerics.bin", "v1-collections.bin",
        "v1-composites.bin", "v1-keyed.bin", "v1-union.bin", "v1-references.bin",
        "v1-absences.bin", "v1-protected.bin", "v0-primitives.bin"
    ];

    // --- the values the fixtures were written from ----------------------------------------------

    internal static FrozenPrimitives Primitives => new()
    {
        Flag = true,
        Byte = 0xA5,
        SByte = -42,
        Short = -12345,
        UShort = 54321,
        Int = -1234567890,
        UInt = 3234567890,
        Long = -1234567890123456789L,
        ULong = 12345678901234567890UL,
        Float = 3.5f,
        Double = -2.718281828459045,
        Decimal = -79228162514264.337593543950335m,
        Char = 'Ж',
        Text = "hello — мир 🌍",
        Choice = FrozenChoice.Seventh,
        Half = (Half)1.5f,
        Int128 = Int128.MinValue + 7,
        UInt128 = UInt128.MaxValue - 7,
        // The wire carries these as int64, but the CLR type is pointer-sized, so the frozen value
        // stays inside 32 bits and the fixture decodes on a 32-bit runtime as well.
        NativeInt = int.MinValue,
        NativeUInt = uint.MaxValue,
        Rune = new Rune(0x1F600),
        BigInteger = BigInteger.Pow(2, 130) - 1
    };

    internal static FrozenTimeAndSystem TimeAndSystem => new()
    {
        Timestamp = new DateTime(2026, 9, 20, 13, 45, 30, 123, DateTimeKind.Utc),
        Offset = new DateTimeOffset(2026, 9, 20, 13, 45, 30, TimeSpan.FromHours(-5)),
        Elapsed = TimeSpan.FromTicks(987654321098765),
        Date = new DateOnly(2026, 9, 20),
        Time = new TimeOnly(13, 45, 30, 123),
        Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Location = new Uri("https://example.org/a%20path?q=1#frag"),
        Release = new Version(1, 0, 0, 0),
        Builder = new StringBuilder("builder text"),
        Culture = CultureInfo.GetCultureInfo("fr-FR"),
        Bits = new BitArray([true, false, true, true, false, false, false, true, true])
    };

    internal static FrozenNumerics Numerics => new()
    {
        Complex = new Complex(1.25, -2.5),
        Vector2 = new Vector2(1f, 2f),
        Vector3 = new Vector3(1f, 2f, 3f),
        Vector4 = new Vector4(1f, 2f, 3f, 4f),
        Quaternion = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f),
        Plane = new Plane(1f, 0f, 0f, -5f),
        Matrix3x2 = new Matrix3x2(1f, 2f, 3f, 4f, 5f, 6f),
        Matrix4x4 = new Matrix4x4(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f)
    };

    internal static FrozenCollections Collections => new()
    {
        Array = [1, 2, 3],
        List = ["a", "b"],
        Dictionary = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 },
        Sorted = new SortedDictionary<int, string> { [2] = "two", [1] = "one" },
        Set = [10, 20, 30],
        Stack = new Stack<int>([1, 2, 3]),
        Queue = new Queue<int>([4, 5, 6]),
        Linked = new LinkedList<int>([7, 8]),
        Immutable = [9, 10],
        ImmutableArray = [11, 12]
    };

    internal static FrozenComposites Composites => new()
    {
        Pair = new KeyValuePair<string, int>("key", 42),
        ValueTuple = (7, "seven"),
        Tuple = Tuple.Create(8, "eight"),
        Lazy = new Lazy<int>(() => 99),
        Grid = new[,] { { 1, 2, 3 }, { 4, 5, 6 } },
        Segment = new ArraySegment<int>([1, 2, 3, 4, 5], 1, 3),
        Memory = new int[] { 20, 21 }.AsMemory()
    };

    internal static FrozenKeyed Keyed => new()
    {
        Number = 5,
        Text = "keyed",
        Numbers = [1, 2, 3]
    };

    internal static FrozenShape Union => new FrozenCircle { Label = "round", Radius = 2.5 };

    internal static FrozenGraph Graph()
    {
        var shared = new FrozenNode { Value = 1 };
        shared.Next = shared;

        return new FrozenGraph { Left = shared, Right = shared };
    }

    internal static FrozenAbsences Absences => new()
    {
        NullText = null,
        NullNumber = null,
        PresentNumber = 0,
        NullList = null,
        EmptyList = [],
        EmptyArray = [],
        EmptyDictionary = new Dictionary<int, int>(),
        EmptyText = string.Empty,
        DefaultImmutableArray = default
    };

    // --- the serializers that read them ---------------------------------------------------------

    internal static BinarySerializer Plain => new();

    internal static BinarySerializer Referencing =>
        new(BinarySerializerOptions.Configure().PreserveReferences().Build());

    internal static BinarySerializer Headerless =>
        new(BinarySerializerOptions.Configure().WithVersion(0).AllowV0Fallback().Build());

    internal static BinarySerializer Protected =>
        new(BinarySerializerOptions.Configure()
            .WithCompression(new Brotli())
            .WithChecksum(new Crc32())
            .WithEncryption(new Aes256Gcm(), ProtectedKey, keyId: "v1-fixture")
            .Build());

    // --- the tests ------------------------------------------------------------------------------

    [Fact]
    public void EveryFixture_IsPresentAndNotEmpty()
    {
        // Guards the suite: a missing file would otherwise surface as one confusing failure rather
        // than as the gap it is.
        Assert.All(Fixtures, name => Assert.NotEmpty(Wire.Fixture(name)));
    }

    [Fact]
    public void V1Primitives_DecodeToTheFrozenValue()
    {
        var restored = Plain.Deserialize<FrozenPrimitives>(Wire.Fixture("v1-primitives.bin"))!;

        AssertPrimitives(Primitives, restored);
    }

    [Fact]
    public void V0Primitives_DecodeToTheSameFrozenValue()
    {
        var restored = Headerless.Deserialize<FrozenPrimitives>(Wire.Fixture("v0-primitives.bin"))!;

        AssertPrimitives(Primitives, restored);
    }

    [Fact]
    public void V0Primitives_AreTheV1PayloadWithoutTheHeader()
    {
        // The two fixtures carry the same value, so version 0 is version 1 with the envelope removed.
        byte[] framed = Wire.Fixture("v1-primitives.bin");
        byte[] bare = Wire.Fixture("v0-primitives.bin");

        Assert.Equal(bare, framed[Wire.PlainHeaderLength..]);
    }

    [Fact]
    public void V1TimeAndSystem_DecodesToTheFrozenValue()
    {
        var expected = TimeAndSystem;
        var restored = Plain.Deserialize<FrozenTimeAndSystem>(Wire.Fixture("v1-time-system.bin"))!;

        Assert.Equal(expected.Timestamp, restored.Timestamp);
        Assert.Equal(DateTimeKind.Utc, restored.Timestamp.Kind);
        Assert.Equal(expected.Offset, restored.Offset);
        Assert.Equal(expected.Offset.Offset, restored.Offset.Offset);
        Assert.Equal(expected.Elapsed, restored.Elapsed);
        Assert.Equal(expected.Date, restored.Date);
        Assert.Equal(expected.Time, restored.Time);
        Assert.Equal(expected.Id, restored.Id);
        Assert.Equal(expected.Location, restored.Location);
        Assert.Equal(expected.Release, restored.Release);
        Assert.Equal(expected.Builder!.ToString(), restored.Builder!.ToString());
        Assert.Equal(expected.Culture, restored.Culture);
        Assert.Equal(expected.Bits!.Length, restored.Bits!.Length);
        Assert.Equal([.. expected.Bits.Cast<bool>()], restored.Bits.Cast<bool>());
    }

    [Fact]
    public void V1Numerics_DecodeToTheFrozenValue()
    {
        var expected = Numerics;
        var restored = Plain.Deserialize<FrozenNumerics>(Wire.Fixture("v1-numerics.bin"))!;

        Assert.Equal(expected.Complex, restored.Complex);
        Assert.Equal(expected.Vector2, restored.Vector2);
        Assert.Equal(expected.Vector3, restored.Vector3);
        Assert.Equal(expected.Vector4, restored.Vector4);
        Assert.Equal(expected.Quaternion, restored.Quaternion);
        Assert.Equal(expected.Plane, restored.Plane);
        Assert.Equal(expected.Matrix3x2, restored.Matrix3x2);
        Assert.Equal(expected.Matrix4x4, restored.Matrix4x4);
    }

    [Fact]
    public void V1Collections_DecodeToTheFrozenValue()
    {
        var expected = Collections;
        var restored = Plain.Deserialize<FrozenCollections>(Wire.Fixture("v1-collections.bin"))!;

        Assert.Equal(expected.Array, restored.Array);
        Assert.Equal(expected.List, restored.List);
        Assert.Equal(expected.Dictionary, restored.Dictionary);
        Assert.Equal(expected.Sorted, restored.Sorted);
        AssertEx.SameContents(expected.Set!, restored.Set);
        AssertEx.PopsInOrder([3, 2, 1], restored.Stack);
        AssertEx.DequeuesInOrder([4, 5, 6], restored.Queue);
        Assert.Equal(expected.Linked, restored.Linked);
        Assert.Equal(expected.Immutable, restored.Immutable);
        // ImmutableArray<T> implements IEquatable<T> as reference equality of the backing array,
        // so contents are compared as a sequence rather than through the struct's own Equals.
        Assert.Equal(expected.ImmutableArray.ToArray(), restored.ImmutableArray.ToArray());
    }

    [Fact]
    public void V1Composites_DecodeToTheFrozenValue()
    {
        var expected = Composites;
        var restored = Plain.Deserialize<FrozenComposites>(Wire.Fixture("v1-composites.bin"))!;

        Assert.Equal(expected.Pair, restored.Pair);
        Assert.Equal(expected.ValueTuple, restored.ValueTuple);
        Assert.Equal(expected.Tuple, restored.Tuple);
        Assert.False(restored.Lazy!.IsValueCreated);
        Assert.Equal(99, restored.Lazy.Value);
        Assert.Equal(expected.Grid, restored.Grid);

        // A memory-like value travels as its elements alone, so it comes back at offset zero over an
        // array exactly as long as the segment.
        Assert.Equal([2, 3, 4], restored.Segment);
        Assert.Equal(0, restored.Segment.Offset);
        Assert.Equal(3, restored.Segment.Array!.Length);
        Assert.Equal([20, 21], restored.Memory.ToArray());
    }

    [Fact]
    public void V1Keyed_DecodesToTheFrozenValue()
    {
        var restored = Plain.Deserialize<FrozenKeyed>(Wire.Fixture("v1-keyed.bin"))!;

        Assert.Equal(5, restored.Number);
        Assert.Equal("keyed", restored.Text);
        Assert.Equal([1, 2, 3], restored.Numbers);
    }

    [Fact]
    public void V1Union_DecodesToTheTaggedRuntimeType()
    {
        var restored = Plain.Deserialize<FrozenShape>(Wire.Fixture("v1-union.bin"));

        var circle = Assert.IsType<FrozenCircle>(restored);
        Assert.Equal("round", circle.Label);
        Assert.Equal(2.5, circle.Radius);
    }

    [Fact]
    public void V1References_RestoreIdentityAndTheCycle()
    {
        var restored = Referencing.Deserialize<FrozenGraph>(Wire.Fixture("v1-references.bin"))!;

        Assert.Same(restored.Left, restored.Right);
        Assert.Same(restored.Left, restored.Left!.Next);
        Assert.Equal(1, restored.Left.Value);
    }

    [Fact]
    public void V1Absences_KeepNullApartFromEmpty()
    {
        var restored = Plain.Deserialize<FrozenAbsences>(Wire.Fixture("v1-absences.bin"))!;

        Assert.Null(restored.NullText);
        Assert.Null(restored.NullNumber);
        Assert.Equal(0, restored.PresentNumber);
        Assert.Null(restored.NullList);
        Assert.NotNull(restored.EmptyList);
        Assert.Empty(restored.EmptyList);
        Assert.NotNull(restored.EmptyArray);
        Assert.Empty(restored.EmptyArray);
        Assert.NotNull(restored.EmptyDictionary);
        Assert.Empty(restored.EmptyDictionary);
        Assert.Equal(string.Empty, restored.EmptyText);
        Assert.True(restored.DefaultImmutableArray.IsDefault);
    }

    [Fact]
    public void V1Protected_DecryptsUnderTheFrozenKey()
    {
        var restored = Protected.Deserialize<FrozenPrimitives>(Wire.Fixture("v1-protected.bin"))!;

        AssertPrimitives(Primitives, restored);
    }

    [Fact]
    public void V1Protected_RefusesTheWrongKey()
    {
        // Guards the fixture above: it proves the key is what decrypts it, not that anything would.
        var other = new BinarySerializer(BinarySerializerOptions.Configure()
            .WithCompression(new Brotli())
            .WithChecksum(new Crc32())
            .WithEncryption(new Aes256Gcm(), new byte[32], keyId: "v1-fixture")
            .Build());

        Assert.Throws<BinaryIntegrityException>(
            () => other.Deserialize<FrozenPrimitives>(Wire.Fixture("v1-protected.bin")));
    }

    private static void AssertPrimitives(FrozenPrimitives expected, FrozenPrimitives restored)
    {
        Assert.Equal(expected.Flag, restored.Flag);
        Assert.Equal(expected.Byte, restored.Byte);
        Assert.Equal(expected.SByte, restored.SByte);
        Assert.Equal(expected.Short, restored.Short);
        Assert.Equal(expected.UShort, restored.UShort);
        Assert.Equal(expected.Int, restored.Int);
        Assert.Equal(expected.UInt, restored.UInt);
        Assert.Equal(expected.Long, restored.Long);
        Assert.Equal(expected.ULong, restored.ULong);
        Assert.Equal(expected.Float, restored.Float);
        Assert.Equal(expected.Double, restored.Double);
        Assert.Equal(expected.Decimal, restored.Decimal);
        Assert.Equal(expected.Char, restored.Char);
        Assert.Equal(expected.Text, restored.Text);
        Assert.Equal(expected.Choice, restored.Choice);
        Assert.Equal(expected.Half, restored.Half);
        Assert.Equal(expected.Int128, restored.Int128);
        Assert.Equal(expected.UInt128, restored.UInt128);
        Assert.Equal(expected.NativeInt, restored.NativeInt);
        Assert.Equal(expected.NativeUInt, restored.NativeUInt);
        Assert.Equal(expected.Rune, restored.Rune);
        Assert.Equal(expected.BigInteger, restored.BigInteger);
    }
}
