using System.Reflection;
using ViShap.Viper.Contracts;

namespace ViShap.Viper.Serialization.Tests.Api;

/// <summary>
/// Pins NAOT-14 (INV-5): the surface that makes a type supported names types and nothing of the
/// traversal. A <see cref="SupportedType"/> exposes its declared type and a <see cref="BinaryTypeInfo{T}"/>
/// its type and its context, and nothing else; every method of <see cref="SupportedTypes"/> returns a
/// <see cref="SupportedType"/> and takes nothing but the two operations of a custom collection or the arms
/// of a union — no codec, shape, count, reader or writer.
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
    public void SupportedTypes_TakesNoTraversalAndReturnsOnlySupportedTypes()
    {
        Type[] admitted = [typeof(Func<>), typeof(Action<,>), typeof(ValueTuple<int, Type>[])];

        var methods = typeof(SupportedTypes).GetMethods(Visible);

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            Assert.Equal(typeof(SupportedType), method.ReturnType);
            Assert.All(method.GetParameters(), parameter =>
            {
                var type = parameter.ParameterType.IsGenericType && !parameter.ParameterType.IsArray
                    ? parameter.ParameterType.GetGenericTypeDefinition()
                    : parameter.ParameterType;

                Assert.Contains(type, admitted);
            });
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
