using ViShap.Viper.Contracts;
using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Contracts;

/// <summary>
/// Pins CTX-10 and CTX-11: a context may add a contract as the function that creates it. The function is
/// not called by the context or by the options built with it, only when an operation first looks the type
/// up, and the contract it creates is kept for every later operation and writes what an instance added
/// directly writes; it is in use from then on. A failure of the function, or a null contract from it, is
/// raised by the operation and not kept. Threads using the type at once all get one kept contract. The
/// function is refused where a contract would be: null, twice for a type, or after options were built.
/// </summary>
public class DeferredContractTests
{
    public sealed class Pair
    {
        public int A { get; set; }

        public string? B { get; set; }
    }

    private static readonly MemberDescription[] PairMembers =
        [new("A", typeof(int), null), new("B", typeof(string), null)];

    private sealed class PairContract : TypeContract<Pair>
    {
        public PairContract()
            : base(MemberLayout.Positional, PairMembers, canBeConstructed: true) =>
            UseAccessors(Table());

        public static MemberAccessors<Pair> Table() => new MemberAccessors<Pair>()
            .Member<int>(static (in Pair value) => value.A, static (ref Pair value, int member) => value.A = member)
            .Member<string?>(static (in Pair value) => value.B, static (ref Pair value, string? member) => value.B = member);

        public void HandOverAgain() => UseAccessors(Table());

        public override Pair Create() => new();

        public override void Write(ref MemberWriter writer, in Pair value)
        {
            writer.Member(value.A);
            writer.Member(value.B);
        }

        public override void ReadPositional(ref MemberReader reader, ref Pair value)
        {
            value.A = reader.Member<int>();
            value.B = reader.Member<string?>();
        }

        public override bool ReadKeyed(ref MemberReader reader, int key, ref Pair value) =>
            throw new NotSupportedException();
    }

    /// <summary>A context adding <see cref="PairContract"/> as a function, which counts its calls and does what the test says.</summary>
    private sealed class DeferringContext : BinarySerializerContext
    {
        private int _calls;

        public DeferringContext(Func<int, PairContract?>? create = null) =>
            Add<Pair>(() =>
            {
                int call = Interlocked.Increment(ref _calls);
                var contract = create is null ? new PairContract() : create(call);
                if (contract is not null)
                    Created.Add(contract);

                return contract!;
            });

        public int Calls => Volatile.Read(ref _calls);

        public System.Collections.Concurrent.ConcurrentBag<PairContract> Created { get; } = [];

        public void AddDirectly() => Add(new PairContract());

        public void AddDeferredAgain() => Add<Pair>(static () => new PairContract());
    }

    private sealed class DirectContext : BinarySerializerContext
    {
        public DirectContext() => Add(new PairContract());
    }

    private sealed class NullFunctionContext : BinarySerializerContext
    {
        public NullFunctionContext() => Add((Func<TypeContract<Pair>>)null!);
    }

    private static BinarySerializer With(BinarySerializerContext context) =>
        new(BinarySerializerOptions.Configure().WithContracts(context).Build());

    private static readonly Pair Value = new() { A = 7, B = "seven" };

    // --- CTX-10: created on first use, once, and kept --------------------------------------------------

    [Fact]
    public void Build_WithADeferredContract_DoesNotCreateIt()
    {
        var context = new DeferringContext();

        _ = With(context);

        Assert.Equal(0, context.Calls);
    }

    [Fact]
    public void Serialize_ATypeAddedAsAFunction_CreatesItsContractOnceForEveryOptionsBuiltWithTheContext()
    {
        var context = new DeferringContext();

        for (int round = 0; round < 3; round++)
        {
            var serializer = With(context);
            var read = serializer.Deserialize<Pair>(serializer.Serialize(Value))!;
            Assert.Equal((7, "seven"), (read.A, read.B));
        }

        Assert.Equal(1, context.Calls);
    }

    [Fact]
    public void Serialize_ATypeAddedAsAFunction_WritesWhatTheSameContractAddedDirectlyWrites()
    {
        Assert.Equal(With(new DirectContext()).Serialize(Value), With(new DeferringContext()).Serialize(Value));
        Assert.Equal(new BinarySerializer().Serialize(Value), With(new DeferringContext()).Serialize(Value));
    }

    [Fact]
    public void Serialize_ATypeAddedAsAFunction_LeavesTheCreatedContractInUse()
    {
        var context = new DeferringContext();
        _ = With(context).Serialize(Value);

        var created = Assert.Single(context.Created);
        AssertEx.Throws<BinaryConfigurationException>("already in use", created.HandOverAgain);
    }

    [Fact]
    public void Serialize_ATypeAddedAsAFunctionUsedOnManyThreadsAtOnce_KeepsOneContract()
    {
        var context = new DeferringContext();
        var serializer = With(context);
        byte[] expected = new BinarySerializer().Serialize(Value);

        var written = Concurrent.Race(_ => serializer.Serialize(Value));

        Assert.All(written, bytes => Assert.Equal(expected, bytes));
        int calls = context.Calls;
        Assert.InRange(calls, 1, written.Length);

        _ = serializer.Serialize(Value);
        Assert.Equal(calls, context.Calls);
    }

    // --- CTX-11: failures are raised where the type is used, and not kept ------------------------------

    [Fact]
    public void Serialize_AFunctionThatThrows_RaisesItsExceptionAndCallsItAgainOnTheNextUse()
    {
        var failure = new BinaryConfigurationException("the contract cannot be described");
        var context = new DeferringContext(call => call == 1 ? throw failure : new PairContract());
        var serializer = With(context);

        Assert.Same(failure, Assert.Throws<BinaryConfigurationException>(() => serializer.Serialize(Value)));

        Assert.Equal(new BinarySerializer().Serialize(Value), serializer.Serialize(Value));
        Assert.Equal(2, context.Calls);
    }

    [Fact]
    public void Serialize_AFunctionThatReturnsNull_ThrowsConfigurationNamingTheType()
    {
        var context = new DeferringContext(_ => null);
        var serializer = With(context);

        AssertEx.Throws<BinaryConfigurationException>(nameof(Pair), () => serializer.Serialize(Value));
        AssertEx.Throws<BinaryConfigurationException>(nameof(Pair), () => serializer.Serialize(Value));
        Assert.Equal(2, context.Calls);
    }

    [Fact]
    public void Add_ANullFunction_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NullFunctionContext());
    }

    [Fact]
    public void Add_ASecondContractForATypeAddedAsAFunction_ThrowsConfigurationNamingIt()
    {
        var context = new DeferringContext();

        AssertEx.Throws<BinaryConfigurationException>(nameof(Pair), context.AddDirectly);
        AssertEx.Throws<BinaryConfigurationException>(nameof(Pair), context.AddDeferredAgain);
    }

    [Fact]
    public void Add_AFunctionAfterOptionsWereBuilt_ThrowsConfiguration()
    {
        var context = new DeferringContext();
        _ = With(context);

        AssertEx.Throws<BinaryConfigurationException>("already in use", context.AddDeferredAgain);
    }
}
