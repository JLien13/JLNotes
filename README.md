# JL Notes

A lightweight WPF sticky-notes app that integrates with Claude Code via shared markdown files.

![JL Notes Icon](src/JLNotes/tray.ico)

## Features

- System tray app with quick-access note panel (remembers its size and position)
- Notes stored as markdown files with YAML frontmatter
- List, grid, and split (master-detail) views with in-place editing
- Project-based organization
- Priority levels (high, medium, low) with visual indicators
- Tag system with color coding
- Search and filter notes
- Clickable links (Ctrl+Click to open) and live `- [ ]` task checkboxes in note bodies
- Links between notes: `[[note-file-name]]` (Obsidian's syntax) opens that note, and each note lists the notes that link to it ("Linked from"). "Copy Link" in the note menu gives you a link to paste
- Nothing is lost: every save, delete and outside edit keeps the old copy. "Earlier Versions..." in the note menu puts one back, Undo appears after a delete, and "Deleted Notes..." in the tray menu brings back older deletes
- `jln`, a command for Claude and other tools to find, write and restore notes safely (see below)
- Paste or drag-drop images straight into a note (inline thumbnails)
- Export notes to Word, embedded images included
- Dark and light themes
- File watcher — notes created externally (e.g. by Claude Code) appear instantly
- Launching the app while it's already running just brings up the existing panel

## How It Works

JL Notes watches a folder of markdown files (`~/.jlnotes/notes/`). Each note is a `.md` file with YAML frontmatter:

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

Claude Code can create notes directly by writing markdown files to the notes folder — they appear in JL Notes immediately. A hand-written file always loads, even with a rough header or none at all (the title then comes from its first heading or its file name), and the app quotes values properly the next time it saves.

## For Claude and other tools: jln

`jln.exe` is installed next to the app (`%LOCALAPPDATA%\Programs\JLNotes\jln.exe`). It
reads and writes notes through the app's own code, so a note it writes always shows
in the app, and whatever it changes is kept in history.

```
jln find [words] [#tag ...] [--project P] [--status open|done]
jln show <note>
jln new --title T [--project P] [--priority high|medium|low] [--tags "a, b"] [--body TEXT]
jln set <note> [--status done] [--title T] [--add-tag T] [--remove-tag T] [--body TEXT] ...
jln append <note> --text TEXT
jln links <note>
jln history <note>        jln deleted        jln restore <note> [version]
```

`<note>` is a note's file name (with or without `.md`, or without its date when only
one note has that name). A value of `-` is read from input.

## History

Before a note is overwritten or deleted, the file as it was goes into
`~/.jlnotes/history/<note file name>/`, pictures included. Edits made outside the app
while it runs (Notepad, Claude writing a file directly) are kept the same way.
Nothing is pruned.

## Installation

Download `JLNotes-Setup-<version>.exe` from [Releases](../../releases) and run it. The installer will download the .NET 10 Desktop Runtime automatically if needed (silent installs included: `/SILENT` upgrades in place and relaunches the app if it was running).

### Options during install:
- Create desktop shortcut
- Run on Windows startup (off by default; can be changed any time in Settings)

### Updates

JL Notes checks GitHub Releases quietly in the background (once an hour at most,
silently skipped when offline). When a newer version is published, an
"Update to X.Y.Z" link appears next to the version number in the panel header;
click it and the app downloads the installer, closes, upgrades in place, and
reopens. Settings also has a "Check for updates" button for an on-demand check.
Notes are never touched by an update.

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build src/JLNotes/JLNotes.csproj
```

### Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

The build is a single script that publishes the app and compiles the installer:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File installer/build.ps1
```

The version comes from `<Version>` in `src/JLNotes/JLNotes.csproj` (the single
source of truth); pass `-Version X.Y.Z` to override, or `-SkipPublish` to reuse
an existing `publish/` folder.

The installer is output to `installer/Output/JLNotes-Setup-<version>.exe`.

Add `-Release` to also publish a GitHub release (tag `v<version>` with the setup
`.exe` attached) through the signed-in `gh` CLI. That release is what the in-app
updater looks for, so a build is not offered to users until it is published
this way. The target repo comes from `<RepositoryUrl>` in the csproj.

## License

[MIT](LICENSE) — free to use, modify, and distribute.
