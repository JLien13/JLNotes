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

Claude Code can create notes directly by writing markdown files to the notes folder — they appear in JL Notes immediately.

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
