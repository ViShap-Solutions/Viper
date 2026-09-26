namespace ViShap.Viper;

/// <summary>
/// Marks a type as a <em>keyed contract</em>: every member is identified on the wire by an explicit
/// key instead of by its position, which lets readers and writers evolve independently.
/// </summary>
/// <remarks>
/// <para>
/// In a contract type every eligible member must carry exactly one of <see cref="BinaryKeyAttribute"/>
/// or <see cref="BinaryIgnoreAttribute"/>. Leaving a member unmarked, or marking it with both, is a
/// contract error and throws <see cref="Exceptions.BinaryTypeException"/> the first time the type is
/// serialized or deserialized.
/// </para>
/// <para>
/// <see cref="BinaryOrderAttribute"/> and <see cref="BinaryIncludeAttribute"/> have no meaning here
/// and are rejected: order comes from the key, and a key already grants inclusion regardless of
/// visibility.
/// </para>
/// <para>
/// A keyed payload tolerates schema drift. A reader skips keys it does not know, and a member whose
/// key is absent from the payload keeps its default value. Adding a member with a fresh key, or
/// removing one, therefore stays compatible in both directions — unlike the default positional
/// layout, where member order <em>is</em> the format.
/// </para>
/// <para>
/// A derived class inherits the contract: it is keyed too, and every member it adds needs its own
/// key, unique across the whole hierarchy. A base class and the types that extend it therefore share
/// one key space, which is what lets a reader holding the base skip a derived member it does not
/// know.
/// </para>
/// <para>
/// Keyed contracts work in every wire format version, but the payload stream must be seekable: each
/// field's length is written ahead of the field and patched once its size is known. Version 1
/// buffers the payload and always satisfies this. Version 0 writes straight to the destination, so
/// there the destination stream itself must be seekable, or the write throws
/// <see cref="NotSupportedException"/>.
/// </para>
/// <example>
/// <code>
/// [BinaryContract]
/// public sealed class Customer
/// {
///     [BinaryKey(1)] public string Name { get; set; } = "";
///     [BinaryKey(2)] public int Age { get; set; }
///     [BinaryIgnore] public string? CachedDisplayName { get; set; }
/// }
/// </code>
/// </example>
/// </remarks>
/// <seealso cref="BinaryKeyAttribute"/>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
public sealed class BinaryContractAttribute : Attribute;
