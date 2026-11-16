using SharpFuzz;
using ViShap.Viper.Serialization.Tests.Hostile.Fuzz;

return args switch
{
    ["--seed", var corpus] => Seed(corpus),
    ["--replay", var corpus] => Replay(corpus),
    [] when Environment.GetEnvironmentVariable("VIPER_FUZZ_TARGET") is { Length: > 0 } name => Fuzz(name),
    _ => Usage(),
};

// Runs one target under libFuzzer: every input it generates is read through every entry point, and a
// broken promise is thrown, which libFuzzer records as a crash with the input that caused it. The target
// is resolved on the first input, because instrumented code runs only once libFuzzer has started.
static int Fuzz(string name)
{
    FuzzTarget? target = null;
    Fuzzer.LibFuzzer.Run(input => (target ??= FuzzCatalog.Named(name)).Run(input.ToArray()));
    return 0;
}

// Writes the valid frames of every target into <corpus>/<target>/, the inputs a fuzzing run starts from.
static int Seed(string corpus)
{
    foreach (var target in FuzzCatalog.All)
    {
        var directory = Directory.CreateDirectory(Path.Combine(corpus, target.Name));
        for (int index = 0; index < target.Seeds.Count; index++)
            File.WriteAllBytes(Path.Combine(directory.FullName, $"seed-{index}.bin"), target.Seeds[index]);
    }

    return 0;
}

// Reads every input of <corpus>/<target>/ through its target and reports each broken promise.
static int Replay(string corpus)
{
    int findings = 0;
    foreach (var target in FuzzCatalog.All)
    {
        var directory = Path.Combine(corpus, target.Name);
        if (!Directory.Exists(directory))
            continue;

        foreach (var file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            try
            {
                target.Run(File.ReadAllBytes(file));
            }
            catch (FuzzFinding finding)
            {
                findings++;
                Console.Error.WriteLine($"{Path.GetRelativePath(corpus, file)}: {finding.Message}");
            }
        }
    }

    return findings == 0 ? 0 : 1;
}

static int Usage()
{
    Console.Error.WriteLine("ViShap.Viper.Fuzz --seed <corpus> | --replay <corpus> | VIPER_FUZZ_TARGET=<target> under libFuzzer");
    Console.Error.WriteLine($"targets: {string.Join(", ", FuzzCatalog.All.Select(target => target.Name))}");
    return 2;
}
