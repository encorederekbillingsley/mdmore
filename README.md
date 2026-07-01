# mdmore

A `more`-style pager for Windows that renders **Markdown** cleanly in the
terminal — headings, bold/italic, lists, tables, blockquotes, code, and links
are styled with color instead of shown as raw `#`, `*`, and backtick markup.

```
mdmore README.md
```

Press **Space** for the next page, **Enter** for the next line, **q** to quit —
just like `more`.

## Features

- Headings, **bold**, *italic*, ~~strikethrough~~, `inline code`
- Bulleted and numbered lists (including nested)
- Block quotes, fenced code blocks, horizontal rules
- Pipe tables (aligned columns)
- Links (text plus URL)
- Word-wraps to your console width
- `more`-style paging (Space / Enter / q)
- Strips styling automatically when output is piped or redirected to a file

## Usage

```
mdmore <file.md>          Render and page a Markdown file
mdmore -n <file.md>       Render without paging
type file.md | mdmore     Read from a pipe (prints without paging)
```

Options: `-n/--no-pager`, `-h/--help`, `-v/--version`.

> Paging requires a real console. When input or output is redirected (a pipe or
> a file), mdmore prints everything at once.

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# Run from source
dotnet run -- sample.md

# Build a standalone, self-contained mdmore.exe (no .NET install needed to run)
./publish.ps1
```

`publish.ps1` produces `dist\mdmore.exe` and offers to put it on your PATH so
you can run `mdmore` from anywhere.

## How it works

[Markdig](https://github.com/xoofx/markdig) parses the Markdown into a syntax
tree; a small custom renderer (`MarkdownConsoleRenderer.cs`) walks that tree and
emits [Spectre.Console](https://spectreconsole.net/) markup lines, wrapped to the
console width. Producing a list of lines is what lets the pager show one screen
at a time.
