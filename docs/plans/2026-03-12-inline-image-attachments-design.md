# Inline Image Attachments — Design Plan

## Summary

Add image attachment support to JL Notes. Images display inline in the note body as clickable filename links with hover-preview tooltips. Click opens in OS image viewer. Drag-and-drop or upload to add. Delete by removing the inline token.

## Storage

### Attachments directory

```
~/.jlnotes/attachments/{note-slug}/
  screenshot.png
  diagram.png
```

- `note-slug` = note filename minus date prefix and `.md` extension
  - e.g., `2026-03-12-pvr-vrt-review-wrong-defaults.md` → `pvr-vrt-review-wrong-defaults`
- Folder created on demand when first image is added to a note
- Folder deleted when note is deleted or when all images are removed

### Frontmatter

New `attachments` field added to YAML frontmatter:

```yaml
---
title: PVR VRT Review Wrong Defaults
project: VasoGuard
attachments: [screenshot.png, diagram.png]
...
---
```

- Source of truth for which files belong to the note
- Synced on save: orphaned entries (no matching token in body) are removed from list and files deleted from disk

### Body tokens

Inline image positions are marked with `{{filename}}` tokens in the body text:

```
Here's the issue I found.

{{screenshot.png}}

And here's the architecture:

{{diagram.png}}
```

- Unambiguous, no collision with markdown syntax
- On load: parsed into inline UI elements in the RichTextBox
- On save: serialized back to `{{filename}}` text in the body

## UI Changes

### Body editor: TextBox → RichTextBox

Replace the current plain `TextBox` for note body with a `RichTextBox` backed by a `FlowDocument`.

- Text content renders as normal `Paragraph`/`Run` elements
- `{{filename}}` tokens render as `InlineUIContainer` elements containing a styled `TextBlock`:
  - Displays the filename as a clickable link (underline, accent color)
  - **Hover:** `ToolTip` with `Image` control showing the attachment (max ~300x300 preview)
  - **Click:** `Process.Start()` with the file path to open in OS default image viewer
- Cursor navigation and text editing work normally around inline elements
- Selecting and deleting an inline element works like deleting any character

### Adding images

**Drag and drop:**
- Enable `AllowDrop` on the RichTextBox
- On drop: accept image files (`.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.webp`)
- Copy file to `~/.jlnotes/attachments/{note-slug}/`
- Handle filename collisions by appending a number (e.g., `screenshot-2.png`)
- Insert `InlineUIContainer` at drop position
- Add filename to the in-memory attachments list

**Upload button (optional):**
- Small image/paperclip button in the expanded edit area
- Opens file picker filtered to image types
- Same behavior as drag-drop (copy, insert at cursor, update list)

### Removing images

- User deletes the inline element by selecting it and pressing delete/backspace (normal text editing)
- On save: body is serialized to text with `{{tokens}}`
- Compare attachments list against tokens in body
- Missing tokens → remove from frontmatter, delete file from disk

### Note deletion

- When a note is deleted, also delete its attachments folder if it exists:
  `~/.jlnotes/attachments/{note-slug}/`

### Note rename

- If a note's title changes (which changes the slug/filename), rename the attachments folder to match the new slug

## Code Changes

### Models/Note.cs
- Add `Attachments` property: `List<string>`
- Update `ParseFromMarkdown()` to parse `attachments` from frontmatter
- Update `ToMarkdown()` to write `attachments` to frontmatter
- Add helper: `GetAttachmentsDir()` → returns `~/.jlnotes/attachments/{slug}/`
- Add helper: `GetSlug()` → extracts slug from filename

### Services/NoteService.cs
- `Save()`: after writing .md, sync attachments — delete orphaned files
- `Delete()`: also delete attachments folder
- `Save()` with title change: rename attachments folder if slug changed
- New: `GetAttachmentPath(Note note, string filename)` → full path to attachment file
- New: `AddAttachment(Note note, string sourceFilePath)` → copy file, return filename (handles collisions)

### ViewModels/NoteItemViewModel.cs
- Replace `EditBody` string property with FlowDocument-aware property
- Add `Attachments` observable collection for UI binding
- `ToggleExpandCommand`: build FlowDocument from body text, parsing `{{tokens}}` into InlineUIContainers
- `SaveEditsCommand`: serialize FlowDocument back to body text with `{{tokens}}`, sync attachments list
- Handle drag-drop: `AddImageCommand` or event handler
- Handle click-to-open: `OpenAttachmentCommand`

### App.xaml (NoteItemTemplate)
- Replace body `TextBox` with `RichTextBox`:
  - Same styling (BgTertiary, TextPrimary, border, padding)
  - `IsDocumentEnabled="True"` for hyperlink/inline interaction
  - `AcceptsReturn="True"`, `AllowDrop="True"`
  - MinHeight/MaxHeight preserved
- Optional: add small upload/paperclip button near body editor

### Helpers (new)
- `FlowDocumentHelper.cs`:
  - `BuildDocument(string body, string attachmentsDir)` → FlowDocument with inline elements
  - `SerializeDocument(FlowDocument doc)` → string with `{{tokens}}`
  - `CreateAttachmentInline(string filename, string filePath)` → InlineUIContainer with styled TextBlock, tooltip, click handler

## Claude Integration

Claude writes notes by creating `.md` files in `~/.jlnotes/notes/`. To include images:

1. Write the image file to `~/.jlnotes/attachments/{note-slug}/filename.png`
2. Add `attachments: [filename.png]` to the frontmatter
3. Place `{{filename.png}}` in the body where the image should appear

The FileSystemWatcher picks up the new/changed `.md` file and the app renders it.

## Migration

- Existing notes without `attachments` field continue to work — defaults to empty list
- No structural changes to `.md` files
- `~/.jlnotes/attachments/` directory created on demand

## Implementation Order

1. **Note model** — add Attachments property, parse/write frontmatter
2. **NoteService** — attachment file management (add, delete, rename, cleanup)
3. **FlowDocumentHelper** — build/serialize documents with inline tokens
4. **NoteItemViewModel** — wire up FlowDocument, drag-drop, save/cancel
5. **App.xaml** — swap TextBox for RichTextBox, styling
6. **Polish** — tooltip sizing, click-to-open, filename collision handling, upload button
