namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>One <c>csharp</c> block of a Markdown page.</summary>
/// <param name="Page">The page, relative to the repository root, with forward slashes.</param>
/// <param name="Ordinal">The block's position among the page's <c>csharp</c> blocks, from 1.</param>
/// <param name="Line">The page line of the block's first code line, from 1.</param>
/// <param name="Code">The block's code, without its fences and without the fence's indentation.</param>
internal sealed record DocumentationExample(string Page, int Ordinal, int Line, string Code)
{
    /// <summary>The name a test row carries: the page and the block's ordinal.</summary>
    public string Name => $"{Page}#{Ordinal}";

    public override string ToString() => Name;
}

/// <summary>
/// Reads the <c>csharp</c> blocks of the consumer documentation — every page of <c>docs/</c>, the
/// repository's <c>README.md</c> and the package READMEs under <c>src/</c> — so a test can compile and
/// run each one against the current code.
/// </summary>
internal static class DocumentationExamples
{
    /// <summary>The info string that marks a block as C#.</summary>
    public const string Language = "csharp";

    /// <summary>Every page in scope, relative to the repository root, in ordinal order.</summary>
    public static IReadOnlyList<string> Pages { get; } = FindPages(SourceTree.RepositoryRoot);

    /// <summary>Every <c>csharp</c> block of every page in scope, in page and block order.</summary>
    public static IReadOnlyList<DocumentationExample> All { get; } =
        [.. Pages.SelectMany(page => Extract(page, File.ReadAllText(Path.Combine(SourceTree.RepositoryRoot, page))))];

    /// <summary>The block a test row names.</summary>
    public static DocumentationExample Named(string name) =>
        All.SingleOrDefault(example => example.Name == name)
        ?? throw new InvalidOperationException($"No documentation example is named '{name}'.");

    /// <summary>
    /// The pages of <paramref name="root"/> the documentation rule covers: every <c>.md</c> under
    /// <c>docs/</c>, <c>README.md</c>, and every <c>*-README.md</c> of a project under <c>src/</c>.
    /// A missing <c>docs/</c> or <c>src/</c> is raised, so the rule can never pass by finding nothing.
    /// </summary>
    public static IReadOnlyList<string> FindPages(string root)
    {
        string docs = Path.Combine(root, "docs");
        string src = Path.Combine(root, "src");

        if (!Directory.Exists(docs) || !Directory.Exists(src))
            throw new InvalidOperationException($"No 'docs' and 'src' directories under '{root}'.");

        IEnumerable<string> pages =
        [
            .. Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories),
            .. Directory.EnumerateDirectories(src).SelectMany(project => Directory.EnumerateFiles(project, "*-README.md")),
            .. File.Exists(Path.Combine(root, "README.md")) ? [Path.Combine(root, "README.md")] : Array.Empty<string>()
        ];

        return [.. pages.Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The <c>csharp</c> blocks of one page. A fence is three or more backticks, indented by any amount;
    /// a block's lines lose the fence's indentation; a block of another language is skipped; and a fence
    /// left open is raised rather than read to the end of the page.
    /// </summary>
    public static IReadOnlyList<DocumentationExample> Extract(string page, string markdown)
    {
        var examples = new List<DocumentationExample>();
        string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            if (!TryOpenFence(lines[index], out string fence, out int indent, out string info))
                continue;

            int opened = index;
            var code = new List<string>();

            for (index++; index < lines.Length && !Closes(lines[index], fence); index++)
                code.Add(Unindent(lines[index], indent));

            if (index == lines.Length)
                throw new InvalidOperationException($"{page}:{opened + 1}: a code fence is never closed.");

            if (info == Language)
                examples.Add(new DocumentationExample(page, examples.Count + 1, opened + 2, string.Join('\n', code)));
        }

        return examples;
    }

    /// <summary>The info string of every fence of a page, in order — what a block declares itself to be.</summary>
    public static IReadOnlyList<string> FenceLanguages(string markdown)
    {
        var languages = new List<string>();
        string[] lines = markdown.ReplaceLineEndings("\n").Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            if (!TryOpenFence(lines[index], out string fence, out _, out string info))
                continue;

            languages.Add(info);
            for (index++; index < lines.Length && !Closes(lines[index], fence); index++)
            {
            }
        }

        return languages;
    }

    private static bool TryOpenFence(string line, out string fence, out int indent, out string info)
    {
        string trimmed = line.TrimStart(' ');
        indent = line.Length - trimmed.Length;
        int ticks = trimmed.Length - trimmed.TrimStart('`').Length;

        fence = new string('`', ticks);
        info = ticks >= 3 ? trimmed[ticks..].Trim() : "";
        return ticks >= 3;
    }

    private static bool Closes(string line, string fence)
    {
        string trimmed = line.Trim(' ');
        return trimmed.Length >= fence.Length && trimmed.All(c => c == '`');
    }

    private static string Unindent(string line, int indent)
    {
        int spaces = line.Length - line.TrimStart(' ').Length;
        return line[Math.Min(spaces, indent)..];
    }
}
