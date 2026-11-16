using ViShap.Viper.Serialization.Tests.Fixtures;

namespace ViShap.Viper.Serialization.Tests.Hostile.Fuzz;

/// <summary>
/// Pins HST-48…HST-50 and HST-53: seeded mutations of valid frames, read through every entry point — the
/// span, the sequence, seekable, forward-only and short-reading streams, the asynchronous stream and pipe,
/// the stream of frames, <c>Populate</c> and the dumper — let only a <see cref="BinarySerializerException"/>
/// out (HST-48); a frame the span reader accepts re-serializes to the same bytes when the model is
/// canonical (HST-49); and a read takes memory in proportion to the bytes it is given (HST-50). Every
/// input the coverage-guided job of <c>.github/workflows/fuzz.yml</c> kept in
/// <c>tests/ViShap.Viper.Fuzz/Corpus</c> is held to the same rules, and the job fuzzes every target
/// from its corpus (HST-53).
/// </summary>
/// <remarks>
/// Each target runs <see cref="Iterations"/> inputs, 200 unless <c>VIPER_FUZZ_ITERATIONS</c> says
/// otherwise, from a seed that <c>VIPER_FUZZ_SEED</c> may replace; a failure names the target, the
/// seed, the iteration and the input in hexadecimal, which is the test to pin it with.
/// </remarks>
public class MutationFuzzTests
{
    private const int DefaultSeed = 20261117;

    private static int Iterations => Setting("VIPER_FUZZ_ITERATIONS", 200);

    private static int Seed => Setting("VIPER_FUZZ_SEED", DefaultSeed);

    public static TheoryData<string> Targets => [.. FuzzCatalog.All.Select(target => target.Name)];

    [Theory]
    [MemberData(nameof(Targets))]
    public void MutatedFrames_KeepEveryPromiseOfTheReadPath(string name)
    {
        var target = FuzzCatalog.Named(name);
        int seed = Seed ^ Stable(name);
        var mutator = new FrameMutator(seed);
        var seeds = target.Seeds;

        foreach (byte[] frame in seeds)
            target.Run(frame);

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            byte[] frame = seeds[iteration % seeds.Count];
            byte[] other = FuzzCatalog.All[iteration % FuzzCatalog.All.Count].Seeds[0];
            byte[] input = mutator.Mutate(frame, other);

            try
            {
                target.Run(input);
            }
            catch (FuzzFinding finding)
            {
                Assert.Fail($"{finding.Message}\nseed {Seed}, iteration {iteration}, input {Convert.ToHexString(input)}\n{finding.InnerException}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void CommittedCorpus_KeepsEveryPromiseOfTheReadPath(string name)
    {
        var target = FuzzCatalog.Named(name);
        string[] inputs = Directory.GetFiles(Path.Combine(Corpus, name));
        Assert.NotEmpty(inputs);

        foreach (string file in inputs.Order(StringComparer.Ordinal))
        {
            try
            {
                target.Run(File.ReadAllBytes(file));
            }
            catch (FuzzFinding finding)
            {
                Assert.Fail($"{Path.GetFileName(file)}: {finding.Message}\n{finding.InnerException}");
            }
        }
    }

    [Fact]
    public void FuzzingWorkflow_FuzzesEveryTargetAndNothingElse()
    {
        string workflow = File.ReadAllText(Path.Combine(SourceTree.RepositoryRoot, ".github", "workflows", "fuzz.yml"));
        string[] matrix = workflow[(workflow.IndexOf("        target:", StringComparison.Ordinal) + "        target:".Length)..]
            .Split('\n')
            .Skip(1)
            .Select(line => line.Trim())
            .TakeWhile(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim())
            .ToArray();

        Assert.Equal(FuzzCatalog.All.Select(target => target.Name), matrix);
        Assert.Equal(
            FuzzCatalog.All.Select(target => target.Name).Order(StringComparer.Ordinal),
            Directory.GetDirectories(Corpus).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FrameMutator_IsReproducibleFromItsSeed()
    {
        byte[] frame = FuzzCatalog.All[0].Seeds[0];
        var first = new FrameMutator(7);
        var second = new FrameMutator(7);

        for (int i = 0; i < 50; i++)
            Assert.Equal(first.Mutate(frame, frame), second.Mutate(frame, frame));
    }

    private static string Corpus => Path.Combine(SourceTree.RepositoryRoot, "tests", "ViShap.Viper.Fuzz", "Corpus");

    private static int Stable(string text)
    {
        int hash = 17;
        foreach (char c in text)
            hash = unchecked(hash * 31 + c);

        return hash;
    }

    private static int Setting(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out int value) ? value : fallback;
}
