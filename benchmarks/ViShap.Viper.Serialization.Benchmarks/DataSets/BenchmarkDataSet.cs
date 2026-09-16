using ViShap.Viper.Serialization.Benchmarks.Models;

namespace ViShap.Viper.Serialization.Benchmarks.DataSets;

public enum DataSetKind { TinyFlat, MediumFlat, CollectionMedium, CollectionLarge, DictionaryHeavy, DeepGraph, UnicodeHeavy, Random, Compressible, ReferenceDag }

public static class BenchmarkDataSet
{
    public static BenchmarkPayload Create(DataSetKind kind)
    {
        var count=kind switch { DataSetKind.CollectionLarge=>20000, DataSetKind.DictionaryHeavy=>12000, DataSetKind.CollectionMedium=>400, DataSetKind.MediumFlat=>50, DataSetKind.TinyFlat=>8, _=>50 };
        var name=kind==DataSetKind.UnicodeHeavy ? string.Concat(Enumerable.Repeat("Привет世界 e\u0301 😀 ",32)) : kind==DataSetKind.Compressible ? new string('A',2048) : kind==DataSetKind.Random ? Convert.ToHexString(Enumerable.Range(0,1024).Select(i=>(byte)((i*73+19)&255)).ToArray()) : "benchmark";
        var payload=new BenchmarkPayload{Id=42,Name=name,CorrelationId=Guid.Parse("11111111-2222-3333-4444-555555555555"),Timestamp=new DateTime(2026,1,2,3,4,5,DateTimeKind.Utc)};
        if (kind==DataSetKind.MediumFlat) payload.Scalars=Enumerable.Range(0,6).Select(i=>new BenchmarkScalar{A=i,B=i*2,C=i*1000,D=$"scalar-{i}",E=i*0.5}).ToList();
        payload.Items=Enumerable.Range(0,count).Select(i=>new BenchmarkItem{Id=i,Value=kind==DataSetKind.Compressible?"repeat":$"item-{i}",Score=i*0.125}).ToList();
        if (kind==DataSetKind.DictionaryHeavy) payload.Map=Enumerable.Range(0,count).ToDictionary(i=>$"key-{i:000000}",i=>new BenchmarkItem{Id=i,Value=$"value-{i}",Score=i*0.25});
        if (kind==DataSetKind.DeepGraph) { var depth=50; var node=new BenchmarkNode{Value=0}; payload.Root=node; for(var i=1;i<depth;i++){node.Next=new BenchmarkNode{Value=i};node=node.Next;} }
        if (kind==DataSetKind.ReferenceDag) { var shared=new BenchmarkItem{Id=7,Value="shared",Score=7}; payload.SharedA=shared;payload.SharedB=shared;payload.Items=new List<BenchmarkItem>{shared,shared,shared}; }
        return payload;
    }
}
