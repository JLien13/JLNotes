using System.IO;
using JLNotes.Models;

namespace JLNotes.Services;

public class NoteService : IDisposable
{
    private readonly string _notesDir;
    private FileSystemWatcher? _watcher;

    public event Action? NotesChanged;

    public NoteService(string notesDir)
    {
        _notesDir = notesDir;
        if (!Directory.Exists(_notesDir))
            Directory.CreateDirectory(_notesDir);
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
        if (string.IsNullOrEmpty(note.FilePath) || !File.Exists(note.FilePath))
        {
            var fileName = note.GenerateFileName();
            note.FilePath = Path.Combine(_notesDir, fileName);
        }
        note.Updated = DateTime.Now;
        File.WriteAllText(note.FilePath, note.ToMarkdown());
    }

    public void Delete(Note note)
    {
        if (File.Exists(note.FilePath))
            File.Delete(note.FilePath);
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
