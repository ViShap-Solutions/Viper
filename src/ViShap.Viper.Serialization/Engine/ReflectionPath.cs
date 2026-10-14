using System.Diagnostics.CodeAnalysis;

namespace ViShap.Viper.Engine;

/// <summary>
/// The reflection path: building the codec, the contract or the union map of a type by reflection on
/// its first use. It is reached only behind <see cref="IsEnabled"/>, which the trimming and AOT
/// analysers treat as the guard of both requirements, so code behind it raises no warning and is
/// removed when the switch is off.
/// </summary>
internal static class ReflectionPath
{
    /// <summary>The name of the runtime switch that turns the reflection path off.</summary>
    public const string SwitchName = "ViShap.Viper.ReflectionPath.IsEnabled";

    public const string UnreferencedCode =
        "The codec of the value's type, and of every type it reaches, is built by reflection over their " +
        "members, constructors and generic definitions, which trimming can remove.";

    public const string DynamicCode =
        "The codec of the value's type, and of every type it reaches, is closed over generic definitions " +
        "at run time, which native AOT cannot guarantee to have compiled.";

    /// <summary>
    /// Whether a type no context offered may be described by reflection. On unless the application
    /// sets the switch off, which the package does for an application published trimmed or with
    /// native AOT.
    /// </summary>
    [FeatureSwitchDefinition(SwitchName)]
    [FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool IsEnabled { get; } = !AppContext.TryGetSwitch(SwitchName, out bool enabled) || enabled;

    /// <summary>The error for a type the reflection path would have described while it is off.</summary>
    public static BinaryConfigurationException Unavailable(Type type) =>
        new($"'{type}' is not offered by any BinarySerializerContext this process has created, and the " +
            $"reflection path is switched off ('{SwitchName}' is false, as it is for an application " +
            "published trimmed or with native AOT). Add the type to a [BinaryContext], or to a type a " +
            "context already lists, and configure the options with that context; or set the MSBuild " +
            "property ViperReflectionEnabled to true.");
}
