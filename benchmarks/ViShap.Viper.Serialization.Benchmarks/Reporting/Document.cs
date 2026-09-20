using System.Text;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// The report, held as structure rather than as text, so the Markdown and the HTML are two renderings
/// of one document and cannot drift apart. Nothing here formats a number: a cell arrives already
/// formatted by the generator that read it out of a raw file.
/// </summary>
internal sealed class Document
{
    private readonly List<Node> _nodes = [];

    internal void Heading(int level, string text) => _nodes.Add(new Node.Heading(level, text));

    internal void Paragraph(string text) => _nodes.Add(new Node.Paragraph(text));

    internal void Bullets(IEnumerable<string> items) => _nodes.Add(new Node.Bullets([.. items]));

    internal void Code(string text) => _nodes.Add(new Node.Code(text));

    internal void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) =>
        _nodes.Add(new Node.Table(headers, rows));

    internal void Image(string path, string caption) => _nodes.Add(new Node.Image(path, caption));

    internal string ToMarkdown()
    {
        var text = new StringBuilder();

        foreach (var node in _nodes)
        {
            switch (node)
            {
                case Node.Heading heading:
                    text.Append('\n').Append(new string('#', heading.Level)).Append(' ')
                        .Append(heading.Text).Append("\n\n");
                    break;

                case Node.Paragraph paragraph:
                    text.Append(paragraph.Text).Append("\n\n");
                    break;

                case Node.Bullets bullets:
                    foreach (var item in bullets.Items)
                    {
                        text.Append("- ").Append(item).Append('\n');
                    }

                    text.Append('\n');
                    break;

                case Node.Code code:
                    text.Append("```text\n").Append(code.Text).Append("\n```\n\n");
                    break;

                case Node.Table table:
                    text.Append("| ").Append(string.Join(" | ", table.Headers)).Append(" |\n");
                    text.Append('|').Append(string.Join("|", table.Headers.Select(_ => "---"))).Append("|\n");

                    foreach (var row in table.Rows)
                    {
                        text.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
                    }

                    text.Append('\n');
                    break;

                case Node.Image image:
                    text.Append("![").Append(image.Caption).Append("](").Append(image.Path).Append(")\n\n");
                    break;
            }
        }

        return text.ToString().TrimStart('\n');
    }

    internal string ToHtml(string title)
    {
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Svg.Escape(title)).Append("</title>\n<style>\n");
        html.Append("""
            :root { color-scheme: light dark; }
            body { margin: 0 auto; padding: 24px 16px 64px; max-width: 1040px;
                   font: 15px/1.6 "Segoe UI", Helvetica, Arial, sans-serif; }
            h1 { font-size: 1.6rem; } h2 { font-size: 1.25rem; margin-top: 2rem; }
            h3 { font-size: 1.05rem; }
            table { border-collapse: collapse; width: 100%; margin: 12px 0 20px; font-size: 13px; }
            th, td { border: 1px solid #c9ced6; padding: 4px 8px; text-align: left; }
            th { background: #f2f4f7; }
            td:not(:first-child) { text-align: right; font-variant-numeric: tabular-nums; }
            pre { background: #f2f4f7; padding: 10px 12px; overflow-x: auto; font-size: 12px; }
            img { max-width: 100%; height: auto; display: block; margin: 8px 0 4px; }
            figcaption { color: #6b7280; font-size: 12px; margin-bottom: 20px; }
            @media (prefers-color-scheme: dark) {
              body { background: #14171c; color: #e6e8eb; }
              th { background: #1f242b; } th, td { border-color: #3a414b; }
              pre { background: #1f242b; } figcaption { color: #9aa2ad; }
            }
            """);
        html.Append("\n</style>\n</head>\n<body>\n");

        foreach (var node in _nodes)
        {
            switch (node)
            {
                case Node.Heading heading:
                    var tag = $"h{Math.Clamp(heading.Level, 1, 4)}";
                    html.Append('<').Append(tag).Append('>').Append(Svg.Escape(heading.Text))
                        .Append("</").Append(tag).Append(">\n");
                    break;

                case Node.Paragraph paragraph:
                    html.Append("<p>").Append(Inline(paragraph.Text)).Append("</p>\n");
                    break;

                case Node.Bullets bullets:
                    html.Append("<ul>\n");

                    foreach (var item in bullets.Items)
                    {
                        html.Append("<li>").Append(Inline(item)).Append("</li>\n");
                    }

                    html.Append("</ul>\n");
                    break;

                case Node.Code code:
                    html.Append("<pre>").Append(Svg.Escape(code.Text)).Append("</pre>\n");
                    break;

                case Node.Table table:
                    html.Append("<table>\n<thead>\n<tr>");

                    foreach (var header in table.Headers)
                    {
                        html.Append("<th>").Append(Svg.Escape(header)).Append("</th>");
                    }

                    html.Append("</tr>\n</thead>\n<tbody>\n");

                    foreach (var row in table.Rows)
                    {
                        html.Append("<tr>");

                        foreach (var cell in row)
                        {
                            html.Append("<td>").Append(Inline(cell)).Append("</td>");
                        }

                        html.Append("</tr>\n");
                    }

                    html.Append("</tbody>\n</table>\n");
                    break;

                case Node.Image image:
                    html.Append("<figure>\n<img src=\"").Append(image.Path).Append("\" alt=\"")
                        .Append(Svg.Escape(image.Caption)).Append("\">\n<figcaption>")
                        .Append(Svg.Escape(image.Caption)).Append("</figcaption>\n</figure>\n");
                    break;
            }
        }

        return html.Append("</body>\n</html>\n").ToString();
    }

    /// <summary>Carries the few inline marks the generator uses, and escapes everything else.</summary>
    private static string Inline(string text)
    {
        var escaped = Svg.Escape(text);

        while (escaped.Contains('`', StringComparison.Ordinal))
        {
            int open = escaped.IndexOf('`', StringComparison.Ordinal);
            int close = escaped.IndexOf('`', open + 1);

            if (close < 0)
            {
                break;
            }

            escaped = escaped[..open] + "<code>" + escaped[(open + 1)..close] + "</code>" + escaped[(close + 1)..];
        }

        return escaped;
    }

    private abstract record Node
    {
        internal sealed record Heading(int Level, string Text) : Node;

        internal sealed record Paragraph(string Text) : Node;

        internal sealed record Bullets(IReadOnlyList<string> Items) : Node;

        internal sealed record Code(string Text) : Node;

        internal sealed record Table(
            IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : Node;

        internal sealed record Image(string Path, string Caption) : Node;
    }
}
