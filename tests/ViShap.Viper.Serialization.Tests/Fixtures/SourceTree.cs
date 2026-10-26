namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// Locates the production sources so a test can assert an invariant that has no runtime symptom —
/// a forbidden catch shape, or a policy call appearing at the wrong layer.
/// </summary>
internal static class SourceTree
{
    /// <summary>The directory holding the solution.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// Every production <c>.cs</c> file of the assemblies that ship at run time, keyed by its path
    /// relative to <c>src/</c>. The source generator runs only in the compiler and is held to its own
    /// tests.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProductionFiles { get; } = Load();

    private static Dictionary<string, string> Load()
    {
        var root = RepositoryRoot;
        var src = Path.Combine(root, "src");

        if (!Directory.Exists(src))
            throw new InvalidOperationException($"No 'src' directory under the repository root '{root}'.");

        var files = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !Path.GetRelativePath(src, path).StartsWith("ViShap.Viper.Generator", StringComparison.Ordinal))
            .ToDictionary(
                path => Path.GetRelativePath(src, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText);

        if (files.Count == 0)
            throw new InvalidOperationException($"No production sources found under '{src}'.");

        return files;
    }

    /// <summary>
    /// Walks up from the test binary to the directory holding the solution. A failure to find it is
    /// raised rather than swallowed, so a source invariant can never pass by not being checked.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.sln").Any())
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file found above '{AppContext.BaseDirectory}'; the source invariants " +
            "cannot be checked from this location.");
    }
}
