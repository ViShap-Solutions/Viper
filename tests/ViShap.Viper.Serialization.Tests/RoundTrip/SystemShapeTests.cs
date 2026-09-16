namespace ViShap.Viper.Serialization.Tests.RoundTrip;

public sealed class SystemShapeRoundTripTests
{
    [Fact] public void NullabilityCoversReferenceNullableAndNullableValueRoots()
    {
        Assert.Null(TestHelpers.RoundTrip<string?>(null));
        Assert.Null(TestHelpers.RoundTrip<List<int>?>(null));
        int? value=42; Assert.Equal(value,TestHelpers.RoundTrip(value));
        int? empty=null; Assert.Null(TestHelpers.RoundTrip(empty));
    }

    [Fact] public void ArraysIncludingEmptyAndNullElements()
    {
        var empty = TestHelpers.RoundTrip(Array.Empty<int>());
        Assert.IsType<int[]>(empty);
        Assert.Empty(empty);

        var ints = TestHelpers.RoundTrip(new[] { 1, 2, 3 });
        Assert.IsType<int[]>(ints); Assert.Equal(new[]{1,2,3}, ints);
        var refs = TestHelpers.RoundTrip<string?>(null);
        Assert.Null(refs);
        var values = TestHelpers.RoundTrip(new string?[]{ "a", null, "😀" });
        Assert.Equal(new string?[]{"a",null,"😀"}, values);
    }

    [Fact] public void MultiDimensionalArraysPreserveRankDimensionsAndElements()
    {
        var value = new int[2,3,2];
        var n = 0;
        for (var i=0;i<2;i++) for(var j=0;j<3;j++) for(var k=0;k<2;k++) value[i,j,k]=++n;
        var actual = TestHelpers.RoundTrip(value);
        Assert.Equal(3, actual.Rank);
        Assert.Equal(value.GetLength(0), actual.GetLength(0));
        Assert.Equal(value.GetLength(1), actual.GetLength(1));
        Assert.Equal(value.GetLength(2), actual.GetLength(2));
        for (var i=0;i<2;i++) for(var j=0;j<3;j++) for(var k=0;k<2;k++) Assert.Equal(value[i,j,k], actual[i,j,k]);
    }

    [Fact] public void MemoryAndReadOnlyMemory()
    {
        var memory = TestHelpers.RoundTrip(new Memory<int>(new[]{1,2,3})); Assert.Equal(new[]{1,2,3}, memory.ToArray());
        var readOnly = TestHelpers.RoundTrip(new ReadOnlyMemory<int>(new[]{4,5,6})); Assert.Equal(new[]{4,5,6}, readOnly.ToArray());
    }

    [Fact] public void ArraySegmentUsesVisibleSegmentOnly()
    {
        var backing = new[]{99,1,2,3,88};
        var value = new ArraySegment<int>(backing, 1, 3);
        var actual = TestHelpers.RoundTrip(value);
        Assert.IsType<ArraySegment<int>>(actual); Assert.Equal(3, actual.Count); Assert.Equal(new[]{1,2,3}, actual.ToArray());
    }

    [Fact] public void ReadOnlySequenceLogicalSequence()
    {
        var value = new ReadOnlySequence<byte>(new byte[]{1,2,3,4});
        var actual = TestHelpers.RoundTrip(value);
        Assert.Equal(4, actual.Length); Assert.Equal(new byte[]{1,2,3,4}, actual.ToArray());

        var first = new TestSequenceSegment(new byte[]{5,6});
        var second = new TestSequenceSegment(new byte[]{7,8,9});
        first.SetNext(second);
        var split = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        var splitActual = TestHelpers.RoundTrip(split);
        Assert.Equal(new byte[]{5,6,7,8,9}, splitActual.ToArray());
    }

    [Fact] public void TuplesAndKeyValuePair()
    {
        var kv = TestHelpers.RoundTrip(new KeyValuePair<string,int>("x", 42)); Assert.Equal("x", kv.Key); Assert.Equal(42, kv.Value);
        var tuple = TestHelpers.RoundTrip(Tuple.Create("x", 42, true)); Assert.Equal(("x",42,true), (tuple.Item1,tuple.Item2,tuple.Item3));
        var vt = TestHelpers.RoundTrip(("x", 42, (true, 7))); Assert.Equal("x", vt.Item1); Assert.Equal(42, vt.Item2); Assert.True(vt.Item3.Item1); Assert.Equal(7, vt.Item3.Item2);
    }

    [Fact] public void TextAndSystemFormatters()
    {
        var sb = TestHelpers.RoundTrip(new StringBuilder("line1\nПривет 😀")); Assert.Equal("line1\nПривет 😀", sb.ToString());
        var culture = TestHelpers.RoundTrip(System.Globalization.CultureInfo.InvariantCulture); Assert.Equal("", culture.Name);
        var namedCulture = TestHelpers.RoundTrip(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")); Assert.Equal("fr-FR", namedCulture.Name);
        var guid = Guid.NewGuid(); Assert.Equal(guid, TestHelpers.RoundTrip(guid));
        var uri = new Uri("https://example.test/a?q=1"); Assert.Equal(uri, TestHelpers.RoundTrip(uri));
        var relative = new Uri("a/b", UriKind.Relative); Assert.Equal(relative, TestHelpers.RoundTrip(relative));
        var version = new Version(1,2,3,4); Assert.Equal(version, TestHelpers.RoundTrip(version));
    }

    [Fact] public void BitArrayBoundaries()
    {
        foreach (var length in new[]{0,1,7,8,9})
        {
            var bits = new BitArray(length);
            for(var i=0;i<length;i++) bits[i]=i%2==0;
            var actual=TestHelpers.RoundTrip(bits);
            Assert.Equal(length, actual.Length);
            for(var i=0;i<length;i++) Assert.Equal(bits[i], actual[i]);
        }
    }

    [Fact] public void LazyValue()
    {
        var actual = TestHelpers.RoundTrip(new Lazy<string>(() => "lazy-value"));
        Assert.IsType<Lazy<string>>(actual); Assert.Equal("lazy-value", actual.Value);
    }

    private sealed class TestSequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public TestSequenceSegment(byte[] data)
        {
            Memory = data;
        }

        public void SetNext(TestSequenceSegment next)
        {
            next.RunningIndex = RunningIndex + Memory.Length;
            Next = next;
        }
    }
}
