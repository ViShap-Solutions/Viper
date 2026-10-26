using System.Runtime.CompilerServices;
using ViShap.Viper.Contracts;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Type contracts written by hand for every shape of the conformance corpus, in the straight-line form
/// a generated contract takes: public members by plain access, non-public ones through
/// <see cref="UnsafeAccessorAttribute"/>, a struct owner assigned in place. They are reached only
/// through a <see cref="BinarySerializerContext"/>, which is how a consumer supplies them.
/// </summary>
public sealed class HandWrittenContracts : BinarySerializerContext
{
    public static HandWrittenContracts Default { get; } = new();

    public HandWrittenContracts()
    {
        Add(new PositionalContract());
        Add(new KeyedContract());
        Add(new DerivedContract());
        Add(new KeyedBaseContract());
        Add(new KeyedDerivedContract());
        Add(new ShadowedContract());
        Add(new OverriddenContract());
        Add(new ShapeContract());
        Add(new CircleContract());
        Add(new SquareContract());
        Add(new PointContract());
        Add(new KeyedPointContract());
    }

    private sealed class PositionalContract() : TypeContract<ConformancePositional>(
        MemberLayout.Positional,
        [
            new("Id", typeof(int), null),
            new("Flag", typeof(bool), null),
            new("Name", typeof(string), null),
            new("Origin", typeof(ConformancePoint), null),
            new("_ratio", typeof(double), null)
        ],
        canBeConstructed: true)
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_ratio")]
        private static extern ref double Ratio(ConformancePositional owner);

        public override ConformancePositional Create() => new();

        public override void Write(ref MemberWriter writer, in ConformancePositional value)
        {
            writer.Member(value.Id);
            writer.Member(value.Flag);
            writer.Member(value.Name);
            writer.Member(value.Origin);
            writer.Member(Ratio(value));
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformancePositional value)
        {
            value.Id = reader.Member<int>();
            value.Flag = reader.Member<bool>();
            value.Name = reader.Member<string>();
            value.Origin = reader.Member<ConformancePoint>();
            Ratio(value) = reader.Member<double>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformancePositional value) =>
            throw new NotSupportedException();
    }

    private sealed class KeyedContract() : TypeContract<ConformanceKeyed>(
        MemberLayout.Keyed,
        [
            new("Id", typeof(int), 1),
            new("_code", typeof(short), 2),
            new("Name", typeof(string), 3),
            new("Defaulted", typeof(int), 5),
            new("Big", typeof(long), 200)
        ],
        canBeConstructed: true)
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_code")]
        private static extern ref short Code(ConformanceKeyed owner);

        public override ConformanceKeyed Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceKeyed value)
        {
            writer.Member(1, value.Id);
            writer.Member(2, Code(value));
            writer.Member(3, value.Name);
            writer.Member(5, value.Defaulted);
            writer.Member(200, value.Big);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceKeyed value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceKeyed value)
        {
            switch (key)
            {
                case 1: value.Id = reader.Member<int>(); return true;
                case 2: Code(value) = reader.Member<short>(); return true;
                case 3: value.Name = reader.Member<string>(); return true;
                case 5: value.Defaulted = reader.Member<int>(); return true;
                case 200: value.Big = reader.Member<long>(); return true;
                default: return false;
            }
        }
    }

    private sealed class DerivedContract() : TypeContract<ConformanceDerived>(
        MemberLayout.Positional,
        [
            new("Alpha", typeof(int), null),
            new("Mike", typeof(int), null),
            new("Zulu", typeof(int), null),
            new("_hidden", typeof(int), null)
        ],
        canBeConstructed: true)
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_hidden")]
        private static extern ref int Hidden(ConformanceBase owner);

        public override ConformanceDerived Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceDerived value)
        {
            writer.Member(value.Alpha);
            writer.Member(value.Mike);
            writer.Member(value.Zulu);
            writer.Member(Hidden(value));
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceDerived value)
        {
            value.Alpha = reader.Member<int>();
            value.Mike = reader.Member<int>();
            value.Zulu = reader.Member<int>();
            Hidden(value) = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceDerived value) =>
            throw new NotSupportedException();
    }

    private sealed class KeyedBaseContract() : TypeContract<ConformanceKeyedBase>(
        MemberLayout.Keyed,
        [new("First", typeof(int), 1), new("Third", typeof(int), 3)],
        canBeConstructed: true)
    {
        public override ConformanceKeyedBase Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceKeyedBase value)
        {
            writer.Member(1, value.First);
            writer.Member(3, value.Third);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceKeyedBase value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceKeyedBase value)
        {
            switch (key)
            {
                case 1: value.First = reader.Member<int>(); return true;
                case 3: value.Third = reader.Member<int>(); return true;
                default: return false;
            }
        }
    }

    private sealed class KeyedDerivedContract() : TypeContract<ConformanceKeyedDerived>(
        MemberLayout.Keyed,
        [new("First", typeof(int), 1), new("Second", typeof(int), 2), new("Third", typeof(int), 3)],
        canBeConstructed: true)
    {
        public override ConformanceKeyedDerived Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceKeyedDerived value)
        {
            writer.Member(1, value.First);
            writer.Member(2, value.Second);
            writer.Member(3, value.Third);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceKeyedDerived value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceKeyedDerived value)
        {
            switch (key)
            {
                case 1: value.First = reader.Member<int>(); return true;
                case 2: value.Second = reader.Member<int>(); return true;
                case 3: value.Third = reader.Member<int>(); return true;
                default: return false;
            }
        }
    }

    private sealed class ShadowedContract() : TypeContract<ConformanceShadowed>(
        MemberLayout.Positional,
        [new("Value", typeof(int), null), new("Value", typeof(string), null)],
        canBeConstructed: true)
    {
        public override ConformanceShadowed Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceShadowed value)
        {
            writer.Member(((ConformanceShadowBase)value).Value);
            writer.Member(value.Value);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceShadowed value)
        {
            ((ConformanceShadowBase)value).Value = reader.Member<int>();
            value.Value = reader.Member<string>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceShadowed value) =>
            throw new NotSupportedException();
    }

    private sealed class OverriddenContract() : TypeContract<ConformanceOverridden>(
        MemberLayout.Positional,
        [new("Value", typeof(int), null), new("Other", typeof(int), null)],
        canBeConstructed: true)
    {
        public override ConformanceOverridden Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceOverridden value)
        {
            writer.Member(value.Value);
            writer.Member(value.Other);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceOverridden value)
        {
            value.Value = reader.Member<int>();
            value.Other = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceOverridden value) =>
            throw new NotSupportedException();
    }

    private sealed class ShapeContract() : TypeContract<ConformanceShape>(
        MemberLayout.Positional,
        [new("Label", typeof(string), null)],
        canBeConstructed: false)
    {
        public override ConformanceShape Create() => throw new NotSupportedException();

        public override void Write(ref MemberWriter writer, in ConformanceShape value) =>
            writer.Member(value.Label);

        public override void ReadPositional(ref MemberReader reader, ref ConformanceShape value) =>
            value.Label = reader.Member<string>();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceShape value) =>
            throw new NotSupportedException();
    }

    private sealed class CircleContract() : TypeContract<ConformanceCircle>(
        MemberLayout.Positional,
        [new("Label", typeof(string), null), new("Radius", typeof(double), null)],
        canBeConstructed: true)
    {
        public override ConformanceCircle Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceCircle value)
        {
            writer.Member(value.Label);
            writer.Member(value.Radius);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceCircle value)
        {
            value.Label = reader.Member<string>();
            value.Radius = reader.Member<double>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceCircle value) =>
            throw new NotSupportedException();
    }

    private sealed class SquareContract() : TypeContract<ConformanceSquare>(
        MemberLayout.Positional,
        [new("Label", typeof(string), null), new("Side", typeof(int), null)],
        canBeConstructed: true)
    {
        public override ConformanceSquare Create() => new();

        public override void Write(ref MemberWriter writer, in ConformanceSquare value)
        {
            writer.Member(value.Label);
            writer.Member(value.Side);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceSquare value)
        {
            value.Label = reader.Member<string>();
            value.Side = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceSquare value) =>
            throw new NotSupportedException();
    }

    private sealed class PointContract() : TypeContract<ConformancePoint>(
        MemberLayout.Positional,
        [new("X", typeof(int), null), new("Y", typeof(int), null)],
        canBeConstructed: true)
    {
        public override ConformancePoint Create() => default;

        public override void Write(ref MemberWriter writer, in ConformancePoint value)
        {
            writer.Member(value.X);
            writer.Member(value.Y);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformancePoint value)
        {
            value.X = reader.Member<int>();
            value.Y = reader.Member<int>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformancePoint value) =>
            throw new NotSupportedException();
    }

    private sealed class KeyedPointContract() : TypeContract<ConformanceKeyedPoint>(
        MemberLayout.Keyed,
        [new("X", typeof(int), 1), new("Tag", typeof(string), 2)],
        canBeConstructed: true)
    {
        public override ConformanceKeyedPoint Create() => default;

        public override void Write(ref MemberWriter writer, in ConformanceKeyedPoint value)
        {
            writer.Member(1, value.X);
            writer.Member(2, value.Tag);
        }

        public override void ReadPositional(ref MemberReader reader, ref ConformanceKeyedPoint value) =>
            throw new NotSupportedException();

        public override bool ReadKeyed(ref MemberReader reader, int key, ref ConformanceKeyedPoint value)
        {
            switch (key)
            {
                case 1: value.X = reader.Member<int>(); return true;
                case 2: value.Tag = reader.Member<string>(); return true;
                default: return false;
            }
        }
    }
}
