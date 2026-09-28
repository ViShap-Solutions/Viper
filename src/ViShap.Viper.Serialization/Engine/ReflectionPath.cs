namespace ViShap.Viper.Engine;

/// <summary>
/// The messages of the requirements the reflection path states: every entry point that encodes or
/// decodes a value of a caller's type builds the codec of that type, and of each type it reaches, by
/// reflection on first use.
/// </summary>
internal static class ReflectionPath
{
    public const string UnreferencedCode =
        "The codec of the value's type, and of every type it reaches, is built by reflection over their " +
        "members, constructors and generic definitions, which trimming can remove.";

    public const string DynamicCode =
        "The codec of the value's type, and of every type it reaches, is closed over generic definitions " +
        "at run time, which native AOT cannot guarantee to have compiled.";
}
