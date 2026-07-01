using Markdig;
using MdMore;
using Spectre.Console;

const string Version = "1.0.0";

// Box-drawing and bullet glyphs need UTF-8; the default OEM code page mangles
// them. Harmless to set even when output is redirected.
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* no console */ }

// ---- Parse arguments ----------------------------------------------------
string? path = null;
var paging = true;

foreach (var arg in args)
{
    switch (arg)
    {
        case "-h" or "--help":
            PrintUsage();
            return 0;
        case "-v" or "--version":
            Console.WriteLine($"mdmore {Version}");
            return 0;
        case "-n" or "--no-pager":
            paging = false;
            break;
        default:
            if (arg.StartsWith('-'))
            {
                Console.Error.WriteLine($"mdmore: unknown option '{arg}'");
                return 2;
            }
            path ??= arg;
            break;
    }
}

// ---- Read input ---------------------------------------------------------
string markdown;
if (path is not null)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"mdmore: cannot open '{path}': file not found");
        return 1;
    }
    markdown = await File.ReadAllTextAsync(path);
}
else if (Console.IsInputRedirected)
{
    markdown = await Console.In.ReadToEndAsync();
}
else
{
    PrintUsage();
    return 2;
}

// ---- Render -------------------------------------------------------------
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
var document = Markdig.Markdown.Parse(markdown, pipeline);

var width = Console.IsOutputRedirected ? 80 : Math.Max(AnsiConsole.Profile.Width - 1, 20);
var lines = MarkdownConsoleRenderer.Render(document, width);

// Paging needs a real console for both output (to size the screen) and input
// (to read keypresses). When either is piped/redirected we just print it all.
var canPage = paging && !Console.IsOutputRedirected && !Console.IsInputRedirected;
Page(lines, canPage);
return 0;

// ---- Pager --------------------------------------------------------------
static void Page(List<string> lines, bool canPage)
{
    var pageRows = canPage ? Math.Max(Console.WindowHeight - 1, 1) : int.MaxValue;
    var shown = 0;

    for (var i = 0; i < lines.Count; i++)
    {
        WriteLine(lines[i]);
        shown++;

        if (!canPage || shown < pageRows || i == lines.Count - 1)
            continue;

        AnsiConsole.Markup("[black on grey] -- More -- [/][grey] Space: page  Enter: line  q: quit [/]");
        var key = Console.ReadKey(intercept: true);
        ErasePromptLine();

        switch (key.Key)
        {
            case ConsoleKey.Q or ConsoleKey.Escape:
                return;
            case ConsoleKey.Enter:
                shown = pageRows - 1; // advance a single line
                break;
            default:
                shown = 0; // Space (or anything else) advances a full page
                break;
        }
    }
}

static void WriteLine(string line)
{
    if (line.Length == 0)
    {
        Console.WriteLine();
        return;
    }

    try
    {
        AnsiConsole.MarkupLine(line);
    }
    catch (InvalidOperationException)
    {
        // Safety net: if a line ever produces markup Spectre rejects, fall back
        // to plain text rather than crashing the whole document mid-render.
        Console.WriteLine(System.Text.RegularExpressions.Regex.Replace(line, @"\[[^\]]*\]", ""));
    }
}

static void ErasePromptLine()
{
    Console.Write('\r');
    Console.Write(new string(' ', Math.Max(Console.WindowWidth - 1, 0)));
    Console.Write('\r');
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        mdmore - view Markdown files cleanly in the terminal, like 'more'.

        Usage:
          mdmore <file.md>            Render and page a Markdown file
          mdmore -n <file.md>         Render without paging
          type file.md | mdmore       Read from a pipe (no paging)

        Options:
          -n, --no-pager    Print everything at once, no paging
          -h, --help        Show this help
          -v, --version     Show version

        While paging:
          Space   next page
          Enter   next line
          q / Esc quit
        """);
}
