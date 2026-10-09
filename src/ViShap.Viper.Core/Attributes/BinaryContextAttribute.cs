namespace ViShap.Viper;

/// <summary>
/// Lists the types a <c>BinarySerializerContext</c> holds contracts for. On a <see langword="partial"/>
/// class deriving from <c>BinarySerializerContext</c>, the ViShap.Viper source generator writes a
/// contract for each listed type and for every member-encoded type it reaches — through members,
/// collection elements, dictionary keys and values, nullable values and <see cref="BinaryUnionAttribute"/>
/// arms — and adds them to the context.
/// </summary>
/// <remarks>
/// The attribute has no effect at run time; it is read by the source generator at build time. Options
/// built with the context use its contracts for those types; any other type is described by reflection,
/// as without a context.
/// <example>
/// <code>
/// [BinaryContext(typeof(Order), typeof(Customer))]
/// public partial class AppContracts : BinarySerializerContext;
///
/// var options = BinarySerializerOptions.Configure()
///     .WithContracts(AppContracts.Default)
///     .Build();
/// </code>
/// </example>
/// </remarks>
/// <param name="types">The root types to generate contracts for.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BinaryContextAttribute(params Type[] types) : Attribute
{
    /// <summary>The root types to generate contracts for.</summary>
    public IReadOnlyList<Type> Types { get; } = types;
}
