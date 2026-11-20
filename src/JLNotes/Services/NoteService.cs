using System.Globalization;
using System.IO;
using JLNotes.Models;

namespace JLNotes.Services;

public class NoteService : IDisposable
{
    private readonly string _notesDir;
    private readonly string _attachmentsRoot;
    private readonly string _historyRoot;
    private FileSystemWatcher? _watcher;

    // Each note's text as this service last read or wrote it. LoadAll compares the
    // folder against it, so an edit or delete made outside the app (Notepad, Claude
    // writing the file itself) still has what it replaced kept in history.
    private Dictionary<string, string> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    // History file names: when the kept text was replaced, sortable as text.
    private const string VersionStamp = "yyyy-MM-dd_HH-mm-ss-fff";

    // Paths this process just wrote, with the time of the write. The watcher
    // skips Changed events for these so the app's OWN saves don't trigger a
    // full reload-from-disk — that reload is what made editing then clicking
    // another note feel laggy. External edits (e.g. Claude) still refresh.
    private readonly Dictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SelfWriteWindow = TimeSpan.FromSeconds(1);

    public event Action? NotesChanged;

    /// <summary>A save wrote over a change this service never read (another program,
    /// or Claude, changed the file while it was open here). The other text is in
    /// history; this lets the app say so.</summary>
    public event Action<Note>? OutsideChangeReplaced;

    public NoteService(string notesDir)
    {
        _notesDir = notesDir;
        if (!Directory.Exists(_notesDir))
            Directory.CreateDirectory(_notesDir);
        _attachmentsRoot = AppPaths.AttachmentsRootFor(_notesDir);
        _historyRoot = AppPaths.HistoryRootFor(_notesDir);
    }

    /// <summary>File names the last LoadAll could not read. Bad headers load fine,
    /// so this is for real read failures (a locked file), which the panel shows
    /// instead of letting a note vanish silently.</summary>
    public IReadOnlyList<string> UnreadableFiles { get; private set; } = [];

    public List<Note> LoadAll()
    {
        var notes = new List<Note>();
        var unreadable = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(_notesDir, "*.md"))
        {
            try
            {
                var content = File.ReadAllText(file);
                notes.Add(Note.ParseFromMarkdown(content, file));
                seen[file] = content;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(Path.GetFileName(file));
                if (_lastSeen.TryGetValue(file, out var before))
                    seen[file] = before; // locked, not gone
            }
        }
        UnreadableFiles = unreadable;

        foreach (var (path, before) in _lastSeen)
        {
            // Whoever changed it may have kept this text already (jln does), maybe
            // not as the newest version, so any kept copy counts.
            if (!seen.TryGetValue(path, out var now) || now != before)
                KeepText(path, before, anyVersion: true);
        }
        _lastSeen = seen;

        return notes.OrderBy(n => n.SortOrder).ThenByDescending(n => n.Created).ToList();
    }

    /// <param name="note">The note to write.</param>
    /// <param name="stampUpdated">False when nothing the user wrote changed, such as a new sort position.</param>
    public void Save(Note note, bool stampUpdated = true)
    {
        string? previous = null;
        string? previousBody = null;
        if (string.IsNullOrEmpty(note.FilePath) || !File.Exists(note.FilePath))
        {
            // Never reuse another note's file: same title on the same day (or two
            // inline-created notes in one minute) used to overwrite each other.
            var fileName = note.GenerateFileName();
            var candidate = Path.Combine(_notesDir, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            // Nor a deleted note's: its history would mix with the new note's.
            for (var n = 2; File.Exists(candidate) || Directory.Exists(HistoryDirOf(candidate)); n++)
                candidate = Path.Combine(_notesDir, $"{stem}-{n}{ext}");
            note.FilePath = candidate;
        }
        else
        {
            previous = File.ReadAllText(note.FilePath);
            previousBody = Note.ParseFromMarkdown(previous, note.FilePath).Body;
        }

        if (stampUpdated)
            note.Updated = DateTime.Now;
        var text = note.ToMarkdown();
        var seenBefore = _lastSeen.TryGetValue(note.FilePath, out var seen);
        var changedOutside = seenBefore && previous != null && previous != seen;
        // A new position in a list over this service's own last write is not worth a version.
        if (previous != null && previous != text && (stampUpdated || !seenBefore || changedOutside))
            KeepText(note.FilePath, previous);
        // Record before writing: the watcher fires asynchronously and may beat
        // the line after WriteAllText, so the entry must already be in place.
        RecordSelfWrite(note.FilePath);
        File.WriteAllText(note.FilePath, text);
        _lastSeen[note.FilePath] = text;
        if (changedOutside)
            OutsideChangeReplaced?.Invoke(note);

        // A save that leaves the body alone (priority, status, sort order) cannot
        // change what the note references. Without this check an image still waiting
        // for its token to reach the body would be deleted.
        if (previousBody != note.Body.Trim())
            DeleteUnreferencedAttachments(note);
    }

    /// <summary>Removes a note, keeping it and its pictures in history first.
    /// Returns the kept version, which <see cref="Restore"/> puts back.</summary>
    public NoteVersion? Delete(Note note)
    {
        NoteVersion? kept = null;
        if (File.Exists(note.FilePath))
        {
            kept = KeepText(note.FilePath, File.ReadAllText(note.FilePath));
            File.Delete(note.FilePath);
        }
        _lastSeen.Remove(note.FilePath);

        var attachDir = GetAttachmentsDir(note);
        if (Directory.Exists(attachDir))
        {
            foreach (var file in Directory.GetFiles(attachDir))
                KeepAttachment(note.FilePath, file);
            Directory.Delete(attachDir, true);
        }
        return kept;
    }

    /// <summary>Notes that are gone from the notes folder, each as its last kept
    /// version, most recently removed first.</summary>
    public IReadOnlyList<NoteVersion> GetDeleted()
    {
        if (!Directory.Exists(_historyRoot)) return [];
        return Directory.GetDirectories(_historyRoot)
            .Select(dir => Path.Combine(_notesDir, Path.GetFileName(dir) + ".md"))
            .Where(path => !File.Exists(path))
            .Select(path => GetVersions(path).FirstOrDefault())
            .OfType<NoteVersion>()
            .OrderByDescending(v => v.SavedAt)
            .ToList();
    }

    /// <summary>
    /// Puts a kept version back where its note lives, as it was, pictures included.
    /// What it replaces is kept first, so a restore can itself be undone. The caller
    /// refreshes (the folder watcher may refresh again, which is harmless).
    /// </summary>
    public void Restore(NoteVersion version)
    {
        var text = File.ReadAllText(version.VersionPath);
        if (File.Exists(version.NotePath))
        {
            var current = File.ReadAllText(version.NotePath);
            if (current == text) return;
            KeepText(version.NotePath, current);
        }
        RecordSelfWrite(version.NotePath);
        File.WriteAllText(version.NotePath, text);
        _lastSeen[version.NotePath] = text;

        var note = Note.ParseFromMarkdown(text, version.NotePath);
        var liveDir = GetAttachmentsDir(note);
        var keptDir = KeptAttachmentsDir(version.NotePath);
        foreach (var token in note.GetAttachmentTokens())
        {
            var kept = Path.Combine(keptDir, token);
            var live = Path.Combine(liveDir, token);
            if (token != Path.GetFileName(token) || File.Exists(live) || !File.Exists(kept)) continue;
            Directory.CreateDirectory(liveDir);
            File.Copy(kept, live); // a copy: other versions may need it too
        }
    }

    /// <summary>Earlier versions of the note at this path, live or deleted, newest first.</summary>
    public IReadOnlyList<NoteVersion> GetVersions(string notePath)
    {
        var dir = HistoryDirOf(notePath);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.md")
            .Select(file => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), VersionStamp,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                ? new NoteVersion(notePath, file, at)
                : null)
            .OfType<NoteVersion>()
            .OrderByDescending(v => v.SavedAt)
            .ToList();
    }

    private static string Stamp(DateTime at) => at.ToString(VersionStamp, CultureInfo.InvariantCulture);

    private string HistoryDirOf(string notePath) =>
        Path.Combine(_historyRoot, Path.GetFileNameWithoutExtension(notePath));

    private string KeptAttachmentsDir(string notePath) => Path.Combine(HistoryDirOf(notePath), "attachments");

    /// <summary>The one way text goes into history. Text already kept as the newest
    /// version (or, with <paramref name="anyVersion"/>, as any version) is not kept again.</summary>
    private NoteVersion KeepText(string notePath, string text, bool anyVersion = false)
    {
        var versions = GetVersions(notePath);
        if ((anyVersion ? versions : versions.Take(1)).FirstOrDefault(v => File.ReadAllText(v.VersionPath) == text) is { } kept)
            return kept;

        var dir = HistoryDirOf(notePath);
        Directory.CreateDirectory(dir);
        var now = DateTime.Now;
        var at = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond)); // what the name can hold
        string path;
        while (File.Exists(path = Path.Combine(dir, Stamp(at) + ".md")))
            at = at.AddMilliseconds(1);
        File.WriteAllText(path, text);
        return new NoteVersion(notePath, path, at);
    }

    /// <summary>Moves a picture into history instead of erasing it. An older kept
    /// picture with the same name and different content stays, under a new name.</summary>
    private void KeepAttachment(string notePath, string file)
    {
        var dir = KeptAttachmentsDir(notePath);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(file));
        if (File.Exists(dest))
        {
            if (File.ReadAllBytes(dest).AsSpan().SequenceEqual(File.ReadAllBytes(file)))
            {
                File.Delete(file);
                return;
            }
            var older = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(dest)}.{Stamp(DateTime.Now)}{Path.GetExtension(dest)}");
            File.Move(dest, older);
        }
        File.Move(file, dest);
    }

    /// <summary>The one answer to where a note's attachments live: a folder named
    /// after its full file name, date included, which no other note can share.</summary>
    public string GetAttachmentsDir(Note note) =>
        Path.Combine(_attachmentsRoot, Path.GetFileNameWithoutExtension(note.FilePath));

    /// <summary>
    /// Attachment folders used to be named without the date, so notes with the same
    /// title shared one. This moves each note's files into its own folder, copying a
    /// file that two notes both use. Files no note references stay where they are, and
    /// a folder no note claims is someone's own and is left alone. Safe to run on every
    /// start: it does nothing once the old folders are gone, and a run that fails
    /// partway finishes on the next start.
    /// </summary>
    public void MigrateAttachmentFolders()
    {
        if (!Directory.Exists(_attachmentsRoot)) return;

        var notes = LoadAll();
        foreach (var oldDir in Directory.GetDirectories(_attachmentsRoot))
        {
            try
            {
                MigrateAttachmentFolder(oldDir, notes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Something has a file open. The originals are untouched until every
                // copy has landed, so the next start picks this folder up again.
            }
        }
    }

    private void MigrateAttachmentFolder(string oldDir, List<Note> notes)
    {
        var folderName = Path.GetFileName(oldDir);
        var isSomeNotesCurrentFolder = notes.Any(n => FolderNameMatches(GetAttachmentsDir(n), folderName));
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in notes.Where(n => n.GetSlug().Equals(folderName, StringComparison.OrdinalIgnoreCase)
                                               && !FolderNameMatches(GetAttachmentsDir(n), folderName)))
        {
            var newDir = GetAttachmentsDir(owner);
            foreach (var token in owner.GetAttachmentTokens().Where(t => Helpers.FlowDocumentHelper.IsAttachment(t, oldDir)))
            {
                Directory.CreateDirectory(newDir);
                File.Copy(Path.Combine(oldDir, token), Path.Combine(newDir, token), overwrite: true);
                copied.Add(token);
            }
        }

        if (isSomeNotesCurrentFolder) return;
        foreach (var token in copied)
            File.Delete(Path.Combine(oldDir, token));
        if (copied.Count > 0 && !Directory.EnumerateFileSystemEntries(oldDir).Any())
            Directory.Delete(oldDir);
    }

    private static bool FolderNameMatches(string dir, string folderName) =>
        Path.GetFileName(dir).Equals(folderName, StringComparison.OrdinalIgnoreCase);

    public string AddAttachment(Note note, string sourceFilePath)
    {
        var dir = GetAttachmentsDir(note);
        Directory.CreateDirectory(dir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(dir, fileName);

        // Handle filename collisions
        var counter = 2;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        while (AttachmentNameTaken(note, fileName))
        {
            fileName = $"{nameWithoutExt}-{counter}{ext}";
            destPath = Path.Combine(dir, fileName);
            counter++;
        }

        File.Copy(sourceFilePath, destPath);
        return fileName;
    }

    public string AddAttachmentFromImage(Note note, System.Windows.Media.Imaging.BitmapSource image)
    {
        var dir = GetAttachmentsDir(note);
        Directory.CreateDirectory(dir);

        var baseName = $"pasted-{DateTime.Now:yyyyMMdd-HHmmss}";
        var fileName = $"{baseName}.png";
        var destPath = Path.Combine(dir, fileName);

        var counter = 2;
        while (AttachmentNameTaken(note, fileName))
        {
            fileName = $"{baseName}-{counter}.png";
            destPath = Path.Combine(dir, fileName);
            counter++;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = File.Create(destPath);
        encoder.Save(stream);
        return fileName;
    }

    /// <summary>A picture name is taken if the note uses it now or history keeps one
    /// by that name; reusing a kept name would make an older version show the new picture.</summary>
    private bool AttachmentNameTaken(Note note, string fileName) =>
        File.Exists(Path.Combine(GetAttachmentsDir(note), fileName))
        || File.Exists(Path.Combine(KeptAttachmentsDir(note.FilePath), fileName));

    private void DeleteUnreferencedAttachments(Note note)
    {
        var dir = GetAttachmentsDir(note);
        if (!Directory.Exists(dir)) return;

        var referenced = new HashSet<string>(note.GetAttachmentTokens(), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!referenced.Contains(Path.GetFileName(file)))
                KeepAttachment(note.FilePath, file); // an older version may still show it
        }

        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
    }

    public void StartWatching()
    {
        _watcher = new FileSystemWatcher(_notesDir, "*.md")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Created += (_, _) => NotesChanged?.Invoke();
        _watcher.Changed += (_, e) => { if (!IsSelfWrite(e.FullPath)) NotesChanged?.Invoke(); };
        _watcher.Deleted += (_, _) => NotesChanged?.Invoke();
        _watcher.Renamed += (_, _) => NotesChanged?.Invoke();
    }

    private void RecordSelfWrite(string path)
    {
        lock (_selfWrites)
            _selfWrites[path] = DateTime.UtcNow;
    }

    // True if WE wrote this path within the suppression window. Consumes expired
    // entries so the dictionary can't grow unbounded.
    private bool IsSelfWrite(string path)
    {
        lock (_selfWrites)
        {
            if (_selfWrites.TryGetValue(path, out var when) && DateTime.UtcNow - when < SelfWriteWindow)
                return true;
            _selfWrites.Remove(path);
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
