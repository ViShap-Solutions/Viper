using System.Reflection;

namespace ViShap.Viper.Serialization.Tests.Exceptions;

/// <summary>
/// Pins EXC-01 and EXC-05: the §8 hierarchy is asserted branch for branch — each type's direct base,
/// not merely that everything descends from the root — and every branch of it is a public type an
/// external assembly can name in a <c>catch</c>.
/// </summary>
public class ExceptionHierarchyTests
{
    /// <summary>
    /// §8, transcribed: each exception against the type it derives from <em>directly</em>. Asserting
    /// the direct base is what makes the table a shape rather than a set — moving
    /// <c>BinaryStreamException</c> under <c>BinaryFormatException</c> would leave every
    /// "derives from <c>BinarySerializerException</c>" assertion green.
    /// </summary>
    private static readonly (Type Exception, Type Base)[] Hierarchy =
    [
        (typeof(BinarySerializerException), typeof(Exception)),
        (typeof(BinaryConfigurationException), typeof(BinarySerializerException)),
        (typeof(BinaryFormatException), typeof(BinarySerializerException)),
        (typeof(BinaryLimitException), typeof(BinaryFormatException)),
        (typeof(BinaryFormatNotSupportedException), typeof(BinarySerializerException)),
        (typeof(BinaryIntegrityException), typeof(BinarySerializerException)),
        (typeof(BinaryEncryptionException), typeof(BinarySerializerException)),
        (typeof(BinaryEncryptionKeyException), typeof(BinaryEncryptionException)),
        (typeof(BinaryStreamException), typeof(BinarySerializerException)),
        (typeof(BinaryTypeException), typeof(BinarySerializerException))
    ];

    public static TheoryData<Type, Type> Branches
    {
        get
        {
            var data = new TheoryData<Type, Type>();
            foreach (var (exception, baseType) in Hierarchy)
                data.Add(exception, baseType);

            return data;
        }
    }

    public static TheoryData<Type> ConcreteBranches
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var (exception, _) in Hierarchy.Where(branch => !branch.Exception.IsAbstract))
                data.Add(exception);

            return data;
        }
    }

    /// <summary>Every exception type the shipped assemblies define, however it is reached.</summary>
    private static Type[] ShippedExceptions() =>
        [.. new[] { typeof(BinarySerializerException).Assembly, typeof(BinarySerializer).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(Exception).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    // --- EXC-01: the hierarchy matches §8 exactly, branch for branch ------------------------------

    [Theory]
    [MemberData(nameof(Branches))]
    public void ExceptionType_DerivesDirectlyFromTheTypeTheContractNames(Type exception, Type expectedBase)
    {
        Assert.Equal(expectedBase, exception.BaseType);
    }

    [Fact]
    public void ExceptionHierarchy_ContainsNoTypeBeyondTheContract()
    {
        // A tenth branch, or a private helper exception, would be public surface the contract does
        // not describe — and a caller could never be told which of them to catch.
        Assert.Equal(
            Hierarchy.Select(branch => branch.Exception).OrderBy(t => t.FullName, StringComparer.Ordinal),
            ShippedExceptions());
    }

    [Fact]
    public void BinarySerializerException_IsTheOnlyAbstractBranch()
    {
        // The root is a category, never a thrown value; every leaf must be instantiable.
        Assert.True(typeof(BinarySerializerException).IsAbstract);
        Assert.All(
            Hierarchy.Where(branch => branch.Exception != typeof(BinarySerializerException)),
            branch => Assert.False(branch.Exception.IsAbstract));
    }

    // --- EXC-05: every branch is public and catchable from outside --------------------------------

    [Theory]
    [MemberData(nameof(Branches))]
    public void ExceptionType_IsPublicAndNotNested(Type exception, Type expectedBase)
    {
        Assert.Equal(expectedBase, exception.BaseType);
        Assert.True(exception.IsPublic);
        Assert.False(exception.IsNested);
    }

    [Theory]
    [MemberData(nameof(ConcreteBranches))]
    public void ExceptionType_ExposesTheTwoPublicConstructorsAnExternalCatchNeedsToRethrow(Type exception)
    {
        // A caller that wraps a Viper failure in its own operation needs both shapes, and a handler
        // testing its own recovery path needs to construct one.
        Assert.NotNull(exception.GetConstructor([typeof(string)]));
        Assert.NotNull(exception.GetConstructor([typeof(string), typeof(Exception)]));

        Assert.All(
            exception.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(constructor.IsPublic));
    }

    [Theory]
    [MemberData(nameof(ConcreteBranches))]
    public void ExceptionType_IsCaughtByAHandlerForTheRootCategory(Type exception)
    {
        var instance = (Exception)Activator.CreateInstance(exception, "constructed by a caller")!;

        Action rethrow = () => throw instance;
        var caught = Assert.Throws(exception, rethrow);

        Assert.IsAssignableFrom<BinarySerializerException>(caught);
        Assert.Equal("constructed by a caller", caught.Message);
    }

    [Theory]
    [MemberData(nameof(ConcreteBranches))]
    public void ExceptionType_PreservesTheInnerExceptionItWasGiven(Type exception)
    {
        var cause = new InvalidOperationException("the underlying failure");

        var instance = (Exception)Activator.CreateInstance(exception, "wrapped", cause)!;

        Assert.Same(cause, instance.InnerException);
    }
}
