using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ViShap.Viper.Generator;

/// <summary>
/// Writes the source of a context: one file with its <c>Default</c> instance and the constructor that
/// adds its contracts, and one file per contract. The code is straight-line — one call per member, a
/// <c>switch</c> over the known keys — and names every type with <c>global::</c>; it holds no loop, no
/// length, no count and no tag, which all belong to the serializer.
/// </summary>
internal static class Emitter
{
    private const string Contracts = "global::ViShap.Viper.Contracts";

    private static readonly string Version =
        typeof(Emitter).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";

    /// <summary>The file that gives the context its <c>Default</c> instance and its constructor.</summary>
    public static string Context(ContextHeader context, EquatableArray<string> contractClasses)
    {
        var writer = new CodeWriter();
        Open(writer, context);

        string self = context.Name;
        writer.Line("/// <summary>The context with the contracts generated for the types it lists and the types they reach.</summary>");
        writer.Line($"public static {self} Default {{ get; }} = new {self}();");
        writer.Line();
        writer.Line("/// <summary>Creates the context and adds its generated contracts.</summary>");
        writer.Line($"public {self}()");
        writer.Open();
        foreach (string contract in contractClasses)
            writer.Line($"Add(new {contract}());");
        writer.Close();

        Close(writer, context);
        return writer.ToString();
    }

    /// <summary>The file of one contract, nested in the context.</summary>
    public static string Contract(ContextHeader context, ContractModel contract)
    {
        var writer = new CodeWriter();
        Open(writer, context);

        string type = contract.TypeName;
        writer.Line($"[global::System.CodeDom.Compiler.GeneratedCode(\"ViShap.Viper.Generator\", \"{Version}\")]");
        writer.Line($"private sealed class {contract.ClassName} : {Contracts}.TypeContract<{type}>");
        writer.Open();

        Constructor(writer, contract);
        writer.Line();
        Create(writer, contract);
        writer.Line();
        Write(writer, contract);
        writer.Line();
        ReadPositional(writer, contract);
        writer.Line();
        ReadKeyed(writer, contract);
        Accessors(writer, contract);

        writer.Close();
        Close(writer, context);
        return writer.ToString();
    }

    private static void Constructor(CodeWriter writer, ContractModel contract)
    {
        writer.Line($"public {contract.ClassName}()");
        writer.Indent();
        writer.Line(": base(");
        writer.Indent();
        writer.Line($"{Contracts}.MemberLayout.{(contract.IsKeyed ? "Keyed" : "Positional")},");
        writer.Line($"new {Contracts}.MemberDescription[]");
        writer.Line("{");
        writer.Indent();
        foreach (var member in contract.Members)
        {
            string key = member.Key is { } value ? value.ToString(CultureInfo.InvariantCulture) : "null";
            writer.Line($"new {Contracts}.MemberDescription(\"{member.Name}\", typeof({member.TypeOfName}), {key}),");
        }
        writer.Outdent();
        writer.Line("},");
        writer.Line($"canBeConstructed: {(contract.CanBeConstructed ? "true" : "false")})");
        writer.Outdent();
        writer.Outdent();
        writer.Line("{");
        writer.Line("}");
    }

    private static void Create(CodeWriter writer, ContractModel contract)
    {
        string body = contract.Create switch
        {
            CreateKind.New => $"new {contract.TypeName}()",
            CreateKind.Accessor => contract.CreateCall!,
            CreateKind.Default => "default",
            _ => "throw new global::System.NotSupportedException()"
        };

        writer.Line($"public override {contract.TypeName} Create() => {body};");
    }

    private static void Write(CodeWriter writer, ContractModel contract)
    {
        writer.Line($"public override void Write(ref {Contracts}.MemberWriter writer, in {contract.TypeName} value)");
        writer.Open();
        foreach (var member in contract.Members)
        {
            writer.Line(member.Key is { } key
                ? $"writer.Member<{member.TypeName}>({key.ToString(CultureInfo.InvariantCulture)}, {member.Get});"
                : $"writer.Member<{member.TypeName}>({member.Get});");
        }
        writer.Close();
    }

    private static void ReadPositional(CodeWriter writer, ContractModel contract)
    {
        string signature = $"public override void ReadPositional(ref {Contracts}.MemberReader reader, ref {contract.TypeName} value)";
        if (contract.IsKeyed)
        {
            writer.Line($"{signature} =>");
            writer.Indent();
            writer.Line("throw new global::System.NotSupportedException();");
            writer.Outdent();
            return;
        }

        writer.Line(signature);
        writer.Open();
        foreach (var member in contract.Members)
            writer.Line(string.Format(CultureInfo.InvariantCulture, member.Set, $"reader.Member<{member.TypeName}>()"));
        writer.Close();
    }

    private static void ReadKeyed(CodeWriter writer, ContractModel contract)
    {
        string signature = $"public override bool ReadKeyed(ref {Contracts}.MemberReader reader, int key, ref {contract.TypeName} value)";
        if (!contract.IsKeyed)
        {
            writer.Line($"{signature} =>");
            writer.Indent();
            writer.Line("throw new global::System.NotSupportedException();");
            writer.Outdent();
            return;
        }

        writer.Line(signature);
        writer.Open();
        writer.Line("switch (key)");
        writer.Open();
        foreach (var member in contract.Members)
        {
            writer.Line($"case {member.Key!.Value.ToString(CultureInfo.InvariantCulture)}:");
            writer.Indent();
            writer.Line(string.Format(CultureInfo.InvariantCulture, member.Set, $"reader.Member<{member.TypeName}>()"));
            writer.Line("return true;");
            writer.Outdent();
        }
        writer.Line("default:");
        writer.Indent();
        writer.Line("return false;");
        writer.Outdent();
        writer.Close();
        writer.Close();
    }

    private static void Accessors(CodeWriter writer, ContractModel contract)
    {
        foreach (var accessor in contract.Accessors.Where(accessor => accessor.GenericClass is null))
        {
            writer.Line();
            writer.Lines(accessor.Declaration.Replace("public static extern", "private static extern"));
        }

        foreach (var group in contract.Accessors
                     .Where(accessor => accessor.GenericClass is not null)
                     .GroupBy(accessor => (accessor.GenericClass!, accessor.GenericParameters!)))
        {
            writer.Line();
            writer.Line($"private static class {group.Key.Item1}<{group.Key.Item2}>");
            writer.Open();
            bool first = true;
            foreach (var accessor in group)
            {
                if (!first)
                    writer.Line();

                writer.Lines(accessor.Declaration);
                first = false;
            }
            writer.Close();
        }
    }

    private static void Open(CodeWriter writer, ContextHeader context)
    {
        writer.Line("// <auto-generated/>");
        writer.Line("#nullable enable");
        writer.Line("#pragma warning disable CS0612, CS0618");
        writer.Line();

        if (context.Namespace is { } ns)
        {
            writer.Line($"namespace {ns}");
            writer.Open();
        }

        foreach (var container in context.ContainingTypes)
        {
            writer.Line($"partial {container.Keyword} {container.Name}");
            writer.Open();
        }

        writer.Line($"partial class {context.Name}");
        writer.Open();
    }

    private static void Close(CodeWriter writer, ContextHeader context)
    {
        writer.Close();

        foreach (var _ in context.ContainingTypes)
            writer.Close();

        if (context.Namespace is not null)
            writer.Close();
    }

    /// <summary>Indented text with <c>\n</c> line endings, whatever the machine.</summary>
    private sealed class CodeWriter
    {
        private readonly StringBuilder _text = new();
        private int _depth;

        public void Line(string line = "")
        {
            if (line.Length > 0)
                _text.Append(' ', _depth * 4).Append(line);

            _text.Append('\n');
        }

        public void Lines(string text)
        {
            foreach (string line in text.Split('\n'))
                Line(line);
        }

        public void Open()
        {
            Line("{");
            _depth++;
        }

        public void Close()
        {
            _depth--;
            Line("}");
        }

        public void Indent() => _depth++;

        public void Outdent() => _depth--;

        public override string ToString() => _text.ToString();
    }

    /// <summary>The hint name of a context's own file.</summary>
    public static string ContextHint(ContextHeader context) => $"{context.HintPrefix}.g.cs";

    /// <summary>The hint name of a contract's file: stable, and distinct per context and per contract.</summary>
    public static string ContractHint(ContextHeader context, ContractModel contract) =>
        $"{context.HintPrefix}.{contract.ClassName}.g.cs";
}
