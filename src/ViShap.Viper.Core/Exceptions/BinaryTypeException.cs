namespace ViShap.Viper.Exceptions;

/// <summary>
/// The CLR type, its contract, or the object graph is invalid for serialization.
/// </summary>
/// <remarks>
/// <para>
/// Typical causes: contradictory attributes on a member, a duplicate key or order, an unmarked member
/// in a keyed contract, a runtime type with no <see cref="BinaryUnionAttribute"/> declaration for the
/// declared type, an unknown union tag, a type that cannot be constructed because it has no
/// parameterless constructor, a delegate, a cycle without reference preservation enabled, or
/// populate-in-place applied to a type that is not member-encoded.
/// </para>
/// <para>
/// These are usually programming errors surfaced the first time a type is used, not data errors, so
/// they are normally fixed with an attribute rather than by validating input.
/// </para>
/// </remarks>
public sealed class BinaryTypeException : BinarySerializerException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the type or contract problem.</param>
    public BinaryTypeException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the type or contract problem.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryTypeException(string message, Exception? innerException)
        : base(message, innerException) { }
}
