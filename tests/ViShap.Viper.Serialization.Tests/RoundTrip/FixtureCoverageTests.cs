using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.RoundTrip;

public sealed class FixtureCoverageTests
{
    [Fact] public void LargeRecordRoundTripsDeterministically() { var v=new LargeRecord{Id=7,Name="large",Values=Enumerable.Range(0,20).ToList(),Metadata=new Dictionary<string,string>{{"a","1"},{"b","2"}}}; var a=TestHelpers.RoundTrip(v); Assert.Equal(v.Id,a.Id);Assert.Equal(v.Name,a.Name);Assert.Equal(v.Values,a.Values);TestHelpers.AssertDictionary(v.Metadata,a.Metadata); }
    [Fact] public void UnicodeFixtureRoundTrips() { var v=new UnicodeFixture(); var a=TestHelpers.RoundTrip(v); Assert.Equal(v.Text,a.Text);Assert.Equal(v.Combining,a.Combining); }
    [Fact] public void CollectionAndDictionaryFixturesRoundTrip() { var c=TestHelpers.RoundTrip(new CollectionFixtures{Values=new(){1,2,3}});Assert.Equal(new[]{1,2,3},c.Values);var d=TestHelpers.RoundTrip(new DictionaryFixtures{Values=new(){{"x",1},{"y",2}}});TestHelpers.AssertDictionary(new Dictionary<string,int>{{"x",1},{"y",2}},d.Values); }
}
