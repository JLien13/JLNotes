using System.IO;
using JLNotes.Models;

namespace JLNotes.Services;

public class NoteService : IDisposable
{
    private readonly string _notesDir;
    private readonly string _attachmentsBaseDir;
    private FileSystemWatcher? _watcher;

    public event Action? NotesChanged;

    public NoteService(string notesDir)
    {
        _notesDir = notesDir;
        if (!Directory.Exists(_notesDir))
            Directory.CreateDirectory(_notesDir);
        _attachmentsBaseDir = Path.Combine(Path.GetDirectoryName(_notesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!, "attachments");
    }

    public List<Note> LoadAll()
    {
        var notes = new List<Note>();
        foreach (var file in Directory.GetFiles(_notesDir, "*.md"))
        {
            try
            {
                var content = File.ReadAllText(file);
                notes.Add(Note.ParseFromMarkdown(content, file));
            }
            catch
            {
                // Skip malformed files
            }
        }
        return notes.OrderBy(n => n.SortOrder).ThenByDescending(n => n.Created).ToList();
    }

    public void Save(Note note)
    {
        var oldSlug = "";
        if (!string.IsNullOrEmpty(note.FilePath) && File.Exists(note.FilePath))
        {
            // Capture old slug before potential rename
            var oldFileName = Path.GetFileNameWithoutExtension(note.FilePath);
            if (oldFileName.Length > 11 && oldFileName[4] == '-' && oldFileName[7] == '-' && oldFileName[10] == '-')
                oldSlug = oldFileName.Substring(11);
            else
                oldSlug = oldFileName;
        }

        if (string.IsNullOrEmpty(note.FilePath) || !File.Exists(note.FilePath))
        {
            var fileName = note.GenerateFileName();
            note.FilePath = Path.Combine(_notesDir, fileName);
        }
        note.Updated = DateTime.Now;
        File.WriteAllText(note.FilePath, note.ToMarkdown());

        // Rename attachments folder if slug changed
        var newSlug = note.GetSlug();
        if (!string.IsNullOrEmpty(oldSlug) && oldSlug != newSlug)
        {
            var oldDir = Path.Combine(_attachmentsBaseDir, oldSlug);
            var newDir = Path.Combine(_attachmentsBaseDir, newSlug);
            if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
                Directory.Move(oldDir, newDir);
        }

        CleanupOrphanedAttachments(note);
    }

    public void Delete(Note note)
    {
        if (File.Exists(note.FilePath))
            File.Delete(note.FilePath);

        var attachDir = GetAttachmentsDir(note);
        if (Directory.Exists(attachDir))
            Directory.Delete(attachDir, true);
    }

    public string GetAttachmentsDir(Note note)
    {
        return Path.Combine(_attachmentsBaseDir, note.GetSlug());
    }

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

    public void CleanupOrphanedAttachments(Note note)
    {
        var dir = GetAttachmentsDir(note);
        if (!Directory.Exists(dir)) return;

        var validFiles = new HashSet<string>(note.Attachments, StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!validFiles.Contains(Path.GetFileName(file)))
                File.Delete(file);
        }

        // Remove empty directory
        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
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
        _watcher.Changed += (_, _) => NotesChanged?.Invoke();
        _watcher.Deleted += (_, _) => NotesChanged?.Invoke();
        _watcher.Renamed += (_, _) => NotesChanged?.Invoke();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
