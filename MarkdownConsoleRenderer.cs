using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;

namespace MdMore;

/// <summary>
/// Walks a parsed Markdig document and produces a flat list of Spectre.Console
/// markup lines, each already wrapped to the target console width. Returning a
/// list of lines (rather than rendering directly) is what lets the pager show
/// the output one screen at a time.
/// </summary>
internal sealed class MarkdownConsoleRenderer
{
    private readonly int _width;

    private MarkdownConsoleRenderer(int width) => _width = Math.Max(width, 20);

    public static List<string> Render(MarkdownDocument document, int width)
    {
        var renderer = new MarkdownConsoleRenderer(width);
        var lines = new List<string>();
        renderer.RenderBlocks(document, lines, indentMarkup: "", indentWidth: 0, tight: false, TextStyle.None);
        return lines;
    }

    /// <summary>
    /// A resolved inline style. Decorations stack, but there is only ever one
    /// foreground color — a nested color (e.g. inline code inside a heading)
    /// overrides the outer one instead of being appended, which is what
    /// Spectre's markup parser requires.
    /// </summary>
    private readonly record struct TextStyle(string? Color, bool Bold, bool Italic, bool Underline, bool Strike)
    {
        public static readonly TextStyle None = default;

        public TextStyle WithColor(string color) => this with { Color = color };
        public TextStyle WithBold() => this with { Bold = true };
        public TextStyle WithItalic() => this with { Italic = true };
        public TextStyle WithUnderline() => this with { Underline = true };
        public TextStyle WithStrike() => this with { Strike = true };

        public bool IsEmpty => Color is null && !Bold && !Italic && !Underline && !Strike;

        public string ToMarkup()
        {
            var parts = new List<string>(5);
            if (Bold) parts.Add("bold");
            if (Italic) parts.Add("italic");
            if (Underline) parts.Add("underline");
            if (Strike) parts.Add("strikethrough");
            if (Color is not null) parts.Add(Color);
            return string.Join(' ', parts);
        }
    }

    private readonly record struct Run(string Text, TextStyle Style);

    // ---- Blocks ----------------------------------------------------------

    private void RenderBlocks(IEnumerable<Block> blocks, List<string> output,
        string indentMarkup, int indentWidth, bool tight, TextStyle textStyle)
    {
        var first = true;
        foreach (var block in blocks)
        {
            if (!first && !tight)
                output.Add("");
            RenderBlock(block, output, indentMarkup, indentWidth, textStyle);
            first = false;
        }
    }

    private void RenderBlock(Block block, List<string> output,
        string indentMarkup, int indentWidth, TextStyle textStyle)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AddWrapped(heading.Inline, output, indentMarkup, indentWidth, HeadingStyle(heading.Level));
                break;

            case ParagraphBlock paragraph:
                AddWrapped(paragraph.Inline, output, indentMarkup, indentWidth, textStyle);
                break;

            case QuoteBlock quote:
                RenderBlocks(quote, output,
                    indentMarkup + "[grey50]│[/] ", indentWidth + 2,
                    tight: false, textStyle.WithColor("grey70"));
                break;

            case ListBlock list:
                RenderList(list, output, indentMarkup, indentWidth, textStyle);
                break;

            case MdTable table:
                RenderTable(table, output, indentMarkup);
                break;

            case CodeBlock code: // covers FencedCodeBlock too
                RenderCode(code, output, indentMarkup);
                break;

            case ThematicBreakBlock:
                output.Add(indentMarkup + $"[grey50]{new string('─', Math.Max(4, _width - indentWidth))}[/]");
                break;

            case HtmlBlock:
                break; // skip raw HTML

            default:
                if (block is LeafBlock leaf && leaf.Inline is not null)
                    AddWrapped(leaf.Inline, output, indentMarkup, indentWidth, textStyle);
                break;
        }
    }

    private void RenderList(ListBlock list, List<string> output,
        string indentMarkup, int indentWidth, TextStyle textStyle)
    {
        var number = ParseStart(list);
        foreach (var item in list.Cast<ListItemBlock>())
        {
            var marker = list.IsOrdered ? $"{number++}." : "•";
            var slot = new string(' ', marker.Length + 1);
            var childIndent = indentMarkup + slot;
            var childWidth = indentWidth + slot.Length;

            var itemLines = new List<string>();
            RenderBlocks(item, itemLines, childIndent, childWidth, tight: true, textStyle);
            if (itemLines.Count == 0)
                itemLines.Add(childIndent);

            // Swap the leading indent slot of the first line for the marker.
            var prefix = indentMarkup + slot;
            var rest = itemLines[0].StartsWith(prefix) ? itemLines[0][prefix.Length..] : itemLines[0];
            itemLines[0] = $"{indentMarkup}[green]{Escape(marker)}[/] {rest}";

            output.AddRange(itemLines);
        }
    }

    private void RenderCode(CodeBlock code, List<string> output, string indentMarkup)
    {
        var text = code.Lines.ToString().Replace("\r", "");
        var rows = text.Split('\n').ToList();
        if (rows.Count > 0 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);

        foreach (var row in rows)
            output.Add($"{indentMarkup}[grey50]│[/] [grey74]{Escape(row)}[/]");
    }

    private void RenderTable(MdTable table, List<string> output, string indentMarkup)
    {
        var rows = table.Cast<MdTableRow>().ToList();
        var grid = rows.Select(row => row.Cast<MdTableCell>().Select(CellText).ToList()).ToList();
        if (grid.Count == 0)
            return;

        var cols = grid.Max(r => r.Count);
        var widths = new int[cols];
        foreach (var r in grid)
            for (var c = 0; c < r.Count; c++)
                widths[c] = Math.Max(widths[c], r[c].Length);

        for (var ri = 0; ri < grid.Count; ri++)
        {
            var cells = grid[ri];
            var parts = new List<string>();
            for (var c = 0; c < cols; c++)
            {
                var value = (c < cells.Count ? cells[c] : "").PadRight(widths[c]);
                parts.Add(rows[ri].IsHeader ? $"[bold]{Escape(value)}[/]" : Escape(value));
            }
            output.Add(indentMarkup + string.Join(" [grey50]│[/] ", parts));

            if (rows[ri].IsHeader)
            {
                var sep = widths.Select(w => new string('─', w));
                output.Add($"{indentMarkup}[grey50]{string.Join("─┼─", sep)}[/]");
            }
        }
    }

    private static string CellText(MdTableCell cell)
    {
        var runs = new List<Run>();
        foreach (var block in cell)
            if (block is LeafBlock { Inline: not null } leaf)
                AppendInlines(leaf.Inline, runs, TextStyle.None);
        return string.Concat(runs.Select(r => r.Text));
    }

    // ---- Inlines ---------------------------------------------------------

    private void AddWrapped(ContainerInline? inline, List<string> output,
        string indentMarkup, int indentWidth, TextStyle style)
    {
        var runs = new List<Run>();
        AppendInlines(inline, runs, style);
        foreach (var line in Wrap(runs, _width - indentWidth))
            output.Add(indentMarkup + line);
    }

    private static void AppendInlines(ContainerInline? container, List<Run> runs, TextStyle style)
    {
        if (container is null)
            return;
        foreach (var inline in container)
            AppendInline(inline, runs, style);
    }

    private static void AppendInline(Inline inline, List<Run> runs, TextStyle style)
    {
        switch (inline)
        {
            case LiteralInline literal:
                runs.Add(new Run(literal.Content.ToString(), style));
                break;

            case EmphasisInline emphasis:
                var nested = emphasis.DelimiterChar == '~' ? style.WithStrike()
                    : emphasis.DelimiterCount >= 2 ? style.WithBold()
                    : style.WithItalic();
                AppendInlines(emphasis, runs, nested);
                break;

            case CodeInline code:
                runs.Add(new Run(code.Content, style.WithColor("yellow")));
                break;

            case LinkInline { IsImage: true } image:
                AppendInlines(image, runs, style.WithColor("grey70"));
                runs.Add(new Run(" (image)", style.WithColor("grey50")));
                break;

            case LinkInline link:
                AppendInlines(link, runs, style.WithColor("blue").WithUnderline());
                if (!string.IsNullOrEmpty(link.Url))
                    runs.Add(new Run($" ({link.Url})", style.WithColor("grey50")));
                break;

            case AutolinkInline autolink:
                runs.Add(new Run(autolink.Url, style.WithColor("blue").WithUnderline()));
                break;

            case LineBreakInline:
                break; // treated as a word boundary by the wrapper

            case HtmlInline:
                break; // skip raw HTML tags

            case ContainerInline container:
                AppendInlines(container, runs, style);
                break;

            default:
                runs.Add(new Run(inline.ToString() ?? "", style));
                break;
        }
    }

    // ---- Helpers ---------------------------------------------------------

    private static List<string> Wrap(List<Run> runs, int width)
    {
        width = Math.Max(width, 10);

        // A "word" is contiguous non-whitespace text that may span several
        // style runs (e.g. "**bold**," is one word made of two segments), so
        // we only ever break lines at real whitespace.
        var words = new List<List<Run>>();
        List<Run>? current = null;
        foreach (var run in runs)
        {
            var text = run.Text;
            var i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    current = null;
                    i++;
                    continue;
                }
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                if (current is null)
                {
                    current = new List<Run>();
                    words.Add(current);
                }
                current.Add(new Run(text[start..i], run.Style));
            }
        }

        var lines = new List<string>();
        var sb = new System.Text.StringBuilder();
        var len = 0;
        foreach (var word in words)
        {
            var wordLen = word.Sum(s => s.Text.Length);
            if (len > 0 && len + 1 + wordLen > width)
            {
                lines.Add(sb.ToString());
                sb.Clear();
                len = 0;
            }
            if (len > 0)
            {
                sb.Append(' ');
                len++;
            }
            foreach (var segment in word)
                sb.Append(Markup(segment));
            len += wordLen;
        }
        if (len > 0 || lines.Count == 0)
            lines.Add(sb.ToString());
        return lines;
    }

    private static string Markup(Run run) =>
        run.Style.IsEmpty ? Escape(run.Text) : $"[{run.Style.ToMarkup()}]{Escape(run.Text)}[/]";

    private static string Escape(string text) => Spectre.Console.Markup.Escape(text);

    private static TextStyle HeadingStyle(int level) => level switch
    {
        1 => TextStyle.None.WithBold().WithUnderline().WithColor("deepskyblue1"),
        2 => TextStyle.None.WithBold().WithColor("deepskyblue1"),
        3 => TextStyle.None.WithBold().WithColor("cyan1"),
        _ => TextStyle.None.WithBold().WithColor("grey85"),
    };

    private static int ParseStart(ListBlock list) =>
        int.TryParse(list.OrderedStart, out var start) ? start : 1;
}
