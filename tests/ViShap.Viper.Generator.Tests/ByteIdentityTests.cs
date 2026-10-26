using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using ViShap.Viper.Generator.Tests.Fixtures;

namespace ViShap.Viper.Generator.Tests;

/// <summary>
/// Pins GEN-04: for every type of a corpus that exercises each rule the generator reproduces —
/// inclusion and exclusion, explicit and ordinal order, inherited, shadowed and overridden members,
/// keys across a hierarchy, structs, unions, generics, nested types, init, required and private
/// setters, private constructors, keyword names, records and collections of all of them — a value
/// filled member by member writes the same bytes through the generated contracts as through
/// reflection, under V1 with and without references and under V0, and reads back to the same bytes.
/// </summary>
public class ByteIdentityTests
{
    private const string Corpus = """
        using System;
        using System.Collections.Generic;
        using ViShap.Viper;

        namespace Corpus;

        public class Simple { public int A { get; set; } public string? B { get; set; } public double C; }
        public class WithNonPublic
        {
            [BinaryInclude] private int _hidden;
            [BinaryInclude] internal string? Internal { get; set; }
            public int Visible { get; private set; }
            public int Init { get; init; }
            private int _excluded;
            public readonly int ReadOnly;
            public int GetOnly => _hidden + _excluded;
        }
        public class Required { public required string Name { get; set; } public int Age { get; set; } }
        public class PrivateCtor { private PrivateCtor() { } public int X { get; set; } }
        [BinaryContract] public class Keyed { [BinaryKey(10)] public int Ten { get; set; } [BinaryKey(1)] private string? _one; [BinaryKey(300)] public List<int>? Big { get; set; } [BinaryIgnore] public int Skip { get; set; } }
        public class KeyedChild : Keyed { [BinaryKey(5)] public long Five { get; set; } }
        public class Ordered { [BinaryOrder(2)] public int Second { get; set; } [BinaryOrder(1)] public int First { get; set; } public int Zed { get; set; } public int Alpha { get; set; } public int alpha { get; set; } }
        public class ShadowBase { public int Value { get; set; } [BinaryInclude] private int _p; }
        public class Shadow : ShadowBase { public new string? Value { get; set; } [BinaryInclude] private int _p; }
        public class VirtualBase { public virtual int V { get; set; } [BinaryOrder(5)] public virtual int W { get; set; } [BinaryIgnore] public virtual int I { get; set; } }
        public class VirtualDerived : VirtualBase { public override int V { get; set; } public override int W => 3; public override int I { get; set; } }
        public struct Point { public int X; public int Y { get; set; } [BinaryInclude] private int _z; }
        [BinaryContract] public struct KeyedPoint { [BinaryKey(1)] public int X; [BinaryKey(2)] public string? Tag; }
        [BinaryUnion(1, typeof(Circle))] [BinaryUnion(2, typeof(Square))] public abstract class Shape { public string? Label { get; set; } }
        public sealed class Circle : Shape { public double R { get; set; } }
        public sealed class Square : Shape { public int Side { get; set; } }
        public class Generic<T> { [BinaryInclude] private T _value = default!; public T? Public { get; set; } public List<T>? Items { get; set; } }
        public class GenericHolder { public Generic<int>? Ints { get; set; } public Generic<string>? Strings { get; set; } }
        public class Outer { public class Nested { public int N { get; set; } } public Nested? Inner { get; set; } }
        public record Rec(int A, string B) { public Rec() : this(0, "") { } }
        public class Keywords { public int @class { get; set; } public string? @event; }
        public enum Color { Red, Green }
        public class Graph
        {
            public Simple? Left { get; set; }
            public List<Shape?>? Shapes { get; set; }
            public Dictionary<string, Point>? Points { get; set; }
            public Point? MaybePoint { get; set; }
            public Keyed[]? KeyedArray { get; set; }
            public (int, Simple?) Tuple { get; set; }
            public Color Color { get; set; }
            public KeyedPoint KeyedPoint { get; set; }
        }

        [BinaryContext(typeof(Graph), typeof(WithNonPublic), typeof(Required), typeof(PrivateCtor), typeof(KeyedChild),
            typeof(Ordered), typeof(Shadow), typeof(VirtualDerived), typeof(GenericHolder), typeof(Outer), typeof(Rec), typeof(Keywords))]
        public partial class CorpusContracts : BinarySerializerContext;
        """;

    private static readonly string[] Roots =
    [
        "Graph", "WithNonPublic", "Required", "PrivateCtor", "Keyed", "KeyedChild", "Ordered", "Shadow", "VirtualDerived",
        "GenericHolder", "Outer", "Outer+Nested", "Rec", "Keywords", "Simple", "Point", "KeyedPoint", "Shape", "Circle"
    ];

    public static TheoryData<int, bool> Configurations() => new() { { 1, false }, { 1, true }, { 0, false } };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void EveryCorpusType_WritesTheSameBytesThroughGeneratedContractsAsThroughReflection(int version, bool references)
    {
        var run = Harness.Run(Corpus);
        Assert.Empty(run.Diagnostics);

        var assembly = run.Load();
        var context = (BinarySerializerContext)assembly.GetType("Corpus.CorpusContracts")!
            .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        var reflected = new BinarySerializer(Options(version, references).Build());
        var generated = new BinarySerializer(Options(version, references).WithContracts(context).RequireGeneratedContracts().Build());

        foreach (string root in Roots)
        {
            var type = assembly.GetType($"Corpus.{root}", throwOnError: true)!;
            object value = Filler.Fill(type, seed: 1, depth: 0)!;

            byte[] expected = Write(reflected, type, value);
            byte[] actual = Write(generated, type, value);
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"'{root}' differs: {Convert.ToHexString(expected)} vs {Convert.ToHexString(actual)}");

            object? read = Read(generated, type, actual);
            Assert.Equal(expected, Write(reflected, type, read!));
        }
    }

    private static BinarySerializerOptionsBuilder Options(int version, bool references) =>
        BinarySerializerOptions.Configure().WithVersion(version).AllowV0Fallback(version == 0).PreserveReferences(references);

    private static byte[] Write(BinarySerializer serializer, Type type, object value) => serializer.SerializeObject(type, value);

    private static object? Read(BinarySerializer serializer, Type type, byte[] bytes) =>
        typeof(ByteIdentityTests).GetMethod(nameof(ReadTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, [serializer, bytes]);

    private static T? ReadTyped<T>(BinarySerializer serializer, byte[] bytes) => serializer.Deserialize<T>(bytes);

    /// <summary>
    /// Fills a value deterministically: every instance field of every level, backing fields included,
    /// gets a value derived from a running seed, so no two members hold the same one.
    /// </summary>
    private static class Filler
    {
        public static object? Fill(Type type, int seed, int depth)
        {
            if (type == typeof(string)) return $"s{seed}";
            if (type == typeof(bool)) return seed % 2 == 0;
            if (type.IsEnum) return Enum.GetValues(type).GetValue(seed % Enum.GetValues(type).Length);
            if (type.IsPrimitive || type == typeof(decimal)) return Convert.ChangeType(seed % 100, type, System.Globalization.CultureInfo.InvariantCulture);
            if (Nullable.GetUnderlyingType(type) is { } underlying) return Fill(underlying, seed, depth);
            if (typeof(Delegate).IsAssignableFrom(type)) return null;

            if (type.IsArray)
            {
                var array = Array.CreateInstance(type.GetElementType()!, 2);
                for (int i = 0; i < 2; i++) array.SetValue(Fill(type.GetElementType()!, seed + i + 1, depth + 1), i);
                return array;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type)!;
                for (int i = 0; i < 2; i++) list.Add(Fill(type.GetGenericArguments()[0], seed + i + 1, depth + 1));
                return list;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var map = (IDictionary)Activator.CreateInstance(type)!;
                for (int i = 0; i < 2; i++) map[Fill(type.GetGenericArguments()[0], seed + i + 1, depth + 1)!] = Fill(type.GetGenericArguments()[1], seed + i + 7, depth + 1);
                return map;
            }

            if (type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
                return Activator.CreateInstance(type, type.GetGenericArguments().Select((argument, i) => Fill(argument, seed + i, depth + 1)).ToArray());

            if (depth > 3)
                return type.IsValueType ? Activator.CreateInstance(type) : null;

            if (type.IsAbstract)
            {
                var arm = type.GetCustomAttributes<BinaryUnionAttribute>(inherit: false).First().DerivedType;
                return Fill(arm, seed, depth);
            }

            object instance = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is { } constructor
                ? constructor.Invoke(null)
                : RuntimeHelpers.GetUninitializedObject(type);

            int next = seed * 10;
            for (var level = type; level is not null && level != typeof(object); level = level.BaseType)
            {
                foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.Name == "<EqualityContract>k__BackingField") continue;
                    field.SetValue(instance, Fill(field.FieldType, ++next, depth + 1));
                }
            }

            return instance;
        }
    }
}
