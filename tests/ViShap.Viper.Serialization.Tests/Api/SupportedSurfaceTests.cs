using System.Reflection;
using ViShap.Viper.Contracts;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins NAOT-14 (INV-5, INV-20): the surface that makes a type supported names types and nothing of the
/// traversal. A <see cref="SupportedType"/> exposes its declared type and a <see cref="BinaryTypeInfo{T}"/>
/// its type and its context, and nothing else; every method of <see cref="SupportedTypes"/> returns a
/// <see cref="SupportedType"/> and takes nothing but the arms of a union, which it holds to the union's
/// attributes — no operation, codec, shape, count, reader or writer an offer could be made to differ by.
/// </summary>
public class SupportedSurfaceTests
{
    private const BindingFlags Visible = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void SupportedType_ExposesItsTypeAndNothingElse()
    {
        Assert.Equal(["Type"], Members(typeof(SupportedType)));
        Assert.True(typeof(SupportedType).IsSealed);
        Assert.Empty(typeof(SupportedType).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void BinaryTypeInfo_ExposesItsTypeAndContextAndNothingElse()
    {
        Assert.Equal(["Context", "Type"], Members(typeof(BinaryTypeInfo<>)));
        Assert.True(typeof(BinaryTypeInfo<>).IsSealed);
        Assert.Empty(typeof(BinaryTypeInfo<>).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void SupportedTypes_TakesNothingButAUnionsArmsAndReturnsOnlySupportedTypes()
    {
        var methods = typeof(SupportedTypes).GetMethods(Visible);

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.Equal(typeof(SupportedType), method.ReturnType);

            if (method.Name == nameof(SupportedTypes.Union))
                Assert.Equal([typeof(ValueTuple<int, Type>[])], method.GetParameters().Select(parameter => parameter.ParameterType));
            else
                Assert.Empty(method.GetParameters());
        });
    }

    private static string[] Members(Type type) =>
        [.. type.GetMembers(Visible)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Where(member => member.DeclaringType == type)
            .Select(member => member.Name)
            .Distinct()
            .Order(StringComparer.Ordinal)];
}
