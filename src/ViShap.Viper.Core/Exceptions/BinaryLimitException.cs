namespace ViShap.Viper.Exceptions;

/// <summary>
/// The data is structurally valid but exceeds a configured resource limit.
/// </summary>
/// <remarks>
/// <para>
/// Raised for nesting depth, array, collection and dictionary sizes, string and blob lengths, the
/// cumulative element, object-graph-node and keyed-field budgets, and the payload, compressed,
/// encrypted and wire byte ceilings. Every limit lives in <c>SerializationLimits</c>, which the
/// serialization package supplies.
/// </para>
/// <para>
/// This is the expected outcome for hostile input, and it is raised <em>before</em> the work it
/// bounds is performed, so a rejected payload never causes the allocation it asked for.
/// </para>
/// <para>
/// It derives from <see cref="BinaryFormatException"/>: a limit breach is a property of the data, not
/// of the configuration. Invalid limit <em>values</em> are <see cref="BinaryConfigurationException"/>.
/// </para>
/// </remarks>
public sealed class BinaryLimitException : BinaryFormatException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Description of the limit that was exceeded.</param>
    public BinaryLimitException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the failure that caused it.</summary>
    /// <param name="message">Description of the limit that was exceeded.</param>
    /// <param name="innerException">The underlying failure, preserved for diagnostics.</param>
    public BinaryLimitException(string message, Exception? innerException) : base(message, innerException) { }
}
