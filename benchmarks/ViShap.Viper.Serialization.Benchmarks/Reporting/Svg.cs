using System.Globalization;
using System.Text;

namespace ViShap.Viper.Serialization.Benchmarks.Reporting;

/// <summary>
/// The pieces every chart of Benchmark-Plan §24 is drawn from. SVG is written directly rather than
/// through a charting package, so a committed chart has no dependency that can stop rendering later,
/// and a README can reference the file as it stands (CHT-14).
/// </summary>
/// <remarks>
/// Each chart paints an explicit light background and dark ink: a viewer embedding it as an image gets
/// no stylesheet of ours, so a chart that relied on the page's colours would be unreadable in one of
/// the two themes.
/// </remarks>
internal static class Svg
{
    internal const string Ink = "#1f2933";
    internal const string Muted = "#6b7280";
    internal const string Grid = "#dfe3e8";
    internal const string Background = "#ffffff";

    /// <summary>
    /// Series colours, distinguishable in both themes and for the common forms of colour blindness.
    /// </summary>
    internal static readonly string[] Series =
    [
        "#2f6f9f", "#b5651d", "#3f7d58", "#8a5fa8", "#a8324a", "#7a7f35",
    ];

    internal static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    internal static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A value with as many digits as it deserves and no more, for an axis or a bar label.</summary>
    internal static string Compact(double value) => Math.Abs(value) switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000:0.##} G",
        >= 1_000_000 => $"{value / 1_000_000:0.##} M",
        >= 1_000 => $"{value / 1_000:0.##} k",
        _ => Number(value),
    };

    internal static void Text(
        StringBuilder svg, double x, double y, string text,
        string fill = Ink, int size = 12, string anchor = "start", bool bold = false)
    {
        svg.Append(CultureInfo.InvariantCulture,
            $"""<text x="{Number(x)}" y="{Number(y)}" fill="{fill}" font-size="{size}" """);
        svg.Append(CultureInfo.InvariantCulture,
            $"""text-anchor="{anchor}" font-family="Segoe UI, Helvetica, Arial, sans-serif" """);
        svg.Append(bold ? """font-weight="600">""" : ">");
        svg.Append(Escape(text)).Append("</text>\n");
    }

    internal static void Rect(
        StringBuilder svg, double x, double y, double width, double height, string fill)
    {
        svg.Append(CultureInfo.InvariantCulture,
            $"""<rect x="{Number(x)}" y="{Number(y)}" width="{Number(width)}" """);
        svg.Append(CultureInfo.InvariantCulture,
            $"""height="{Number(height)}" fill="{fill}" />""").Append('\n');
    }

    internal static void Line(
        StringBuilder svg, double x1, double y1, double x2, double y2, string stroke, double width = 1)
    {
        svg.Append(CultureInfo.InvariantCulture,
            $"""<line x1="{Number(x1)}" y1="{Number(y1)}" x2="{Number(x2)}" y2="{Number(y2)}" """);
        svg.Append(CultureInfo.InvariantCulture,
            $"""stroke="{stroke}" stroke-width="{Number(width)}" />""").Append('\n');
    }

    internal static StringBuilder Open(double width, double height, string title, string subtitle)
    {
        var svg = new StringBuilder();

        svg.Append(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{Number(width)}" height="{Number(height)}" """);
        svg.Append(CultureInfo.InvariantCulture,
            $"""viewBox="0 0 {Number(width)} {Number(height)}" role="img">""").Append('\n');

        svg.Append("<title>").Append(Escape(title)).Append("</title>\n");
        svg.Append("<desc>").Append(Escape(subtitle)).Append("</desc>\n");

        Rect(svg, 0, 0, width, height, Background);
        Text(svg, 16, 26, title, Ink, 15, bold: true);
        Text(svg, 16, 44, subtitle, Muted, 11);

        return svg;
    }

    internal static string Close(StringBuilder svg) => svg.Append("</svg>\n").ToString();
}

/// <summary>
/// A horizontal bar chart. Bars run sideways because a cell's label is a profile and a dataset, which
/// does not fit under a vertical bar.
/// </summary>
internal sealed class BarChart(string title, string unit, bool lowerIsBetter)
{
    private const double Width = 960;
    private const double Left = 250;
    private const double Right = 90;
    private const double Top = 64;
    private const double RowHeight = 22;

    private readonly List<(string Label, double Value, int Colour)> _bars = [];

    internal void Add(string label, double value, int colour = 0) => _bars.Add((label, value, colour));

    internal string Render()
    {
        double height = Top + (_bars.Count * RowHeight) + 44;
        double plot = Width - Left - Right;
        double max = _bars.Count > 0 ? _bars.Max(bar => bar.Value) : 1;

        if (max <= 0)
        {
            max = 1;
        }

        var subtitle = $"{unit} — {(lowerIsBetter ? "lower is better" : "higher is better")}";
        var svg = Svg.Open(Width, height, title, subtitle);

        for (var index = 0; index < _bars.Count; index++)
        {
            var (label, value, colour) = _bars[index];
            double y = Top + (index * RowHeight);
            double length = Math.Max(value / max * plot, value > 0 ? 1 : 0);

            Svg.Text(svg, Left - 8, y + 15, label, Svg.Ink, 11, "end");
            Svg.Rect(svg, Left, y + 4, length, RowHeight - 8, Svg.Series[colour % Svg.Series.Length]);
            Svg.Text(svg, Left + length + 6, y + 15, Svg.Compact(value), Svg.Muted, 10);
        }

        Svg.Line(svg, Left, Top, Left, Top + (_bars.Count * RowHeight), Svg.Grid);
        Svg.Text(svg, Left, height - 16, $"0 … {Svg.Compact(max)} {unit}", Svg.Muted, 10);

        return Svg.Close(svg);
    }
}

/// <summary>
/// A line chart for the scaling curves of §19. The x axis is logarithmic because every sweep multiplies
/// rather than adds, and a linear axis would put four of five points on top of each other.
/// </summary>
internal sealed class LineChart(string title, string xLabel, string yLabel)
{
    private const double Width = 960;
    private const double Height = 420;
    private const double Left = 78;
    private const double Right = 170;
    private const double Top = 70;
    private const double Bottom = 54;

    private readonly List<(string Name, List<(double X, double Y)> Points)> _series = [];

    internal void Add(string name, IEnumerable<(double X, double Y)> points) =>
        _series.Add((name, [.. points.OrderBy(point => point.X)]));

    internal string Render()
    {
        var all = _series.SelectMany(series => series.Points).ToList();

        if (all.Count == 0)
        {
            return Svg.Close(Svg.Open(Width, 120, title, "no data"));
        }

        double plotWidth = Width - Left - Right;
        double plotHeight = Height - Top - Bottom;

        double minX = Math.Max(all.Min(point => point.X), 1);
        double maxX = Math.Max(all.Max(point => point.X), minX * 10);
        double maxY = Math.Max(all.Max(point => point.Y), double.Epsilon);

        double logMin = Math.Log10(minX);
        double logMax = Math.Log10(maxX);

        double X(double value) => Left + ((Math.Log10(Math.Max(value, 1)) - logMin) / (logMax - logMin) * plotWidth);
        double Y(double value) => Top + plotHeight - (value / maxY * plotHeight);

        var svg = Svg.Open(Width, Height, title, $"{yLabel} against {xLabel} — logarithmic x axis");

        // Four horizontal guides, so a reader can read a value off the chart without a tooltip.
        for (var step = 0; step <= 4; step++)
        {
            double value = maxY / 4 * step;
            double y = Y(value);

            Svg.Line(svg, Left, y, Left + plotWidth, y, Svg.Grid);
            Svg.Text(svg, Left - 8, y + 4, Svg.Compact(value), Svg.Muted, 10, "end");
        }

        foreach (var point in all.Select(point => point.X).Distinct().OrderBy(value => value))
        {
            Svg.Text(svg, X(point), Top + plotHeight + 18, Svg.Compact(point), Svg.Muted, 10, "middle");
        }

        Svg.Text(svg, Left + (plotWidth / 2), Height - 12, xLabel, Svg.Ink, 11, "middle");
        Svg.Text(svg, 16, Top - 10, yLabel, Svg.Ink, 11);

        for (var index = 0; index < _series.Count; index++)
        {
            var (name, points) = _series[index];
            var colour = Svg.Series[index % Svg.Series.Length];

            var path = new StringBuilder();

            for (var step = 0; step < points.Count; step++)
            {
                path.Append(step == 0 ? 'M' : 'L')
                    .Append(Svg.Number(X(points[step].X)))
                    .Append(' ')
                    .Append(Svg.Number(Y(points[step].Y)));
            }

            svg.Append(CultureInfo.InvariantCulture,
                $"""<path d="{path}" fill="none" stroke="{colour}" stroke-width="2" />""").Append('\n');

            foreach (var point in points)
            {
                svg.Append(CultureInfo.InvariantCulture,
                    $"""<circle cx="{Svg.Number(X(point.X))}" cy="{Svg.Number(Y(point.Y))}" r="3" fill="{colour}" />""")
                    .Append('\n');
            }

            Svg.Rect(svg, Width - Right + 10, Top + (index * 20), 10, 10, colour);
            Svg.Text(svg, Width - Right + 26, Top + (index * 20) + 9, name, Svg.Ink, 10);
        }

        Svg.Line(svg, Left, Top, Left, Top + plotHeight, Svg.Grid);
        Svg.Line(svg, Left, Top + plotHeight, Left + plotWidth, Top + plotHeight, Svg.Grid);

        return Svg.Close(svg);
    }
}
