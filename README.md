# JL Notes

A lightweight WPF sticky-notes app that integrates with Claude Code via shared markdown files.

![JL Notes Icon](src/JLNotes/tray.ico)

## Features

- System tray app with quick-access note panel
- Notes stored as markdown files with YAML frontmatter
- Project-based organization
- Priority levels (high, medium, low) with visual indicators
- Tag system with color coding
- Search and filter notes
- File watcher — notes created externally (e.g. by Claude Code) appear instantly

## How It Works

JL Notes watches a folder of markdown files (`~/.claude-notes/notes/`). Each note is a `.md` file with YAML frontmatter:

```markdown
---
title: Fix the login bug
project: MyApp
priority: high
status: open
tags: [bug, auth]
created: 2026-03-12T10:00:00
updated: 2026-03-12T10:00:00
---

## Context
Details about the note...
```

Claude Code can create notes directly by writing markdown files to the notes folder — they appear in JL Notes immediately.

## Installation

Download `JLNotes-Setup.exe` from [Releases](../../releases) and run it. The installer will download the .NET 10 Desktop Runtime automatically if needed.

### Options during install:
- Create desktop shortcut
- Run on Windows startup

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build src/JLNotes/JLNotes.csproj
```

### Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bash
dotnet publish src/JLNotes/JLNotes.csproj -c Release -r win-x64 --self-contained false -o publish
iscc installer/setup.iss
```

The installer will be output to `output/JLNotes-Setup.exe`.

## License

[MIT](LICENSE) — free to use, modify, and distribute.
