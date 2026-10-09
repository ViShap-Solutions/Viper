namespace ViShap.Viper.Generator.Tests.Fixtures;

/// <summary>
/// Compares generated source with the reviewed copy in <c>Snapshots/</c>. A missing or different copy
/// fails the test and leaves the actual source beside it as <c>.received.txt</c>, to be reviewed and
/// renamed; a snapshot changes only when the emission is meant to change.
/// </summary>
internal static class Snapshot
{
    private static readonly string Version = typeof(ContractGenerator).Assembly.GetName().Version!.ToString();

    public static void Match(string name, string actual)
    {
        string directory = Path.Combine(Repository.Root, "tests", "ViShap.Viper.Generator.Tests", "Snapshots");
        string verified = Path.Combine(directory, name + ".verified.txt");
        string received = Path.Combine(directory, name + ".received.txt");

        string normalized = actual.Replace($"\"ViShap.Viper.Generator\", \"{Version}\"", "\"ViShap.Viper.Generator\", \"<version>\"");

        if (!File.Exists(verified))
        {
            File.WriteAllText(received, normalized);
            Assert.Fail($"No snapshot '{name}'; the generated source was written to {received} for review.");
        }

        string expected = File.ReadAllText(verified).Replace("\r\n", "\n");
        if (expected != normalized)
        {
            File.WriteAllText(received, normalized);
            Assert.Equal(expected, normalized);
        }
        else if (File.Exists(received))
        {
            File.Delete(received);
        }
    }
}

/// <summary>The repository the tests run from.</summary>
internal static class Repository
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Viper.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The repository root (Viper.sln) was not found above the test output.");
    }
}
