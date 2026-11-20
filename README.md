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
- The same notes on several PCs: point each one at a shared Dropbox folder in Settings (see below)
- Paste or drag-drop images straight into a note (inline thumbnails)
- Export notes to Word, embedded images included
- Dark and light themes
- File watcher — notes created externally (e.g. by Claude Code) appear instantly
- Launching the app while it's already running just brings up the existing panel

## How It Works

JL Notes watches a folder of markdown files: the `notes` folder inside the notes folder
(`~/.jlnotes` unless Settings picks another). Each note is a `.md` file with YAML frontmatter:

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

Claude Code can create notes directly by writing markdown files to that `notes` folder. They appear in JL Notes immediately. A hand-written file always loads, even with a rough header or none at all (the title then comes from its first heading or its file name), and the app quotes values properly the next time it saves.

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
jln where                 (the notes folder; note files are in its notes folder)
```

`<note>` is a note's file name (with or without `.md`, or without its date when only
one note has that name). A value of `-` is read from input.

Tools that read note files themselves should ask `jln where` for the notes folder (the
files are in its `notes` folder) rather than assume `~/.jlnotes`, since Settings can move
the notes.

## History

Before a note is overwritten or deleted, the file as it was goes into
`history/<note file name>/` in the notes folder, pictures included. Edits made outside the app
while it runs (Notepad, Claude writing a file directly) are kept the same way.
Nothing is pruned.

## Several PCs

Settings > Notes folder > Change... picks where the notes, pictures, history and project
list live. Pick the same folder in Dropbox on each PC and they all show the same notes
(Syncthing works too; other sync tools' conflict files are not recognized). Window places
and the other settings stay with each PC.

- **First PC:** make a new, empty folder in Dropbox and pick it. The notes are copied
  there and the app restarts. The old folder is left as it was, as a backup. A folder
  with other things in it is refused, so notes never mix with other files.
- **Every other PC:** pick the same folder (the one holding `notes`, not `notes` itself).
  Its notes are used, and any notes that PC had are added: notes the folder lacks are
  copied in, except ones deleted there; where both have a note, the newer one is kept,
  and the other goes to Earlier Versions if it is just an earlier version, or waits as a
  second version to choose from if it was changed separately.
- **Changed in two places at once** (two PCs within the few seconds before the sync tool
  catches up, or the app and Claude): nothing is written over. Both versions are kept,
  the note shows "2 versions", and opening it on any PC shows them side by side:
  keep this one, keep the other, or combine both (every line of both and the tags of
  both, with the title, project, priority and status of the one in the note). The one not
  kept goes to Earlier Versions. Dropbox "conflicted copy" files and Syncthing `.sync-conflict-` files are
  handled this way and never show up as extra notes.
- **Typing in a note that changes under you:** if another PC deletes it, or puts a
  different note with the same name in its place, what you typed is kept as a note of
  its own, pictures included.
- **Folder missing at start** (Dropbox not set up on this PC yet, or the folder moved):
  the app asks before using anything else, and never quietly starts an empty folder.
  If you choose this PC's own folder for now, the app offers to go back once the folder
  has notes again, adding anything written meanwhile. `jln` refuses too, rather than
  writing notes into a stray folder.

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
