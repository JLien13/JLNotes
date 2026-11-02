using System.IO;
using JLNotes.Models;

namespace JLNotes.Services;

public class NoteService : IDisposable
{
    private readonly string _notesDir;
    private readonly string _attachmentsRoot;
    private FileSystemWatcher? _watcher;

    // Paths this process just wrote, with the time of the write. The watcher
    // skips Changed events for these so the app's OWN saves don't trigger a
    // full reload-from-disk — that reload is what made editing then clicking
    // another note feel laggy. External edits (e.g. Claude) still refresh.
    private readonly Dictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SelfWriteWindow = TimeSpan.FromSeconds(1);

    public event Action? NotesChanged;

    public NoteService(string notesDir)
    {
        _notesDir = notesDir;
        if (!Directory.Exists(_notesDir))
            Directory.CreateDirectory(_notesDir);
        _attachmentsRoot = AppPaths.AttachmentsRootFor(_notesDir);
    }

    /// <summary>File names the last LoadAll could not read. Bad headers load fine,
    /// so this is for real read failures (a locked file), which the panel shows
    /// instead of letting a note vanish silently.</summary>
    public IReadOnlyList<string> UnreadableFiles { get; private set; } = [];

    public List<Note> LoadAll()
    {
        var notes = new List<Note>();
        var unreadable = new List<string>();
        foreach (var file in Directory.GetFiles(_notesDir, "*.md"))
        {
            try
            {
                var content = File.ReadAllText(file);
                notes.Add(Note.ParseFromMarkdown(content, file));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(Path.GetFileName(file));
            }
        }
        UnreadableFiles = unreadable;
        return notes.OrderBy(n => n.SortOrder).ThenByDescending(n => n.Created).ToList();
    }

    /// <param name="note">The note to write.</param>
    /// <param name="stampUpdated">False when nothing the user wrote changed, such as a new sort position.</param>
    public void Save(Note note, bool stampUpdated = true)
    {
        string? previousBody = null;
        if (string.IsNullOrEmpty(note.FilePath) || !File.Exists(note.FilePath))
        {
            // Never reuse another note's file: same title on the same day (or two
            // inline-created notes in one minute) used to overwrite each other.
            var fileName = note.GenerateFileName();
            var candidate = Path.Combine(_notesDir, fileName);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            for (var n = 2; File.Exists(candidate); n++)
                candidate = Path.Combine(_notesDir, $"{stem}-{n}{ext}");
            note.FilePath = candidate;
        }
        else
        {
            previousBody = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath).Body;
        }

        if (stampUpdated)
            note.Updated = DateTime.Now;
        // Record before writing: the watcher fires asynchronously and may beat
        // the line after WriteAllText, so the entry must already be in place.
        RecordSelfWrite(note.FilePath);
        File.WriteAllText(note.FilePath, note.ToMarkdown());

        // A save that leaves the body alone (priority, status, sort order) cannot
        // change what the note references. Without this check an image still waiting
        // for its token to reach the body would be deleted.
        if (previousBody != note.Body.Trim())
            DeleteUnreferencedAttachments(note);
    }

    public void Delete(Note note)
    {
        if (File.Exists(note.FilePath))
            File.Delete(note.FilePath);

        var attachDir = GetAttachmentsDir(note);
        if (Directory.Exists(attachDir))
            Directory.Delete(attachDir, true);
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
        while (File.Exists(destPath))
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
        while (File.Exists(destPath))
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

    private void DeleteUnreferencedAttachments(Note note)
    {
        var dir = GetAttachmentsDir(note);
        if (!Directory.Exists(dir)) return;

        var referenced = new HashSet<string>(note.GetAttachmentTokens(), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!referenced.Contains(Path.GetFileName(file)))
                File.Delete(file);
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
