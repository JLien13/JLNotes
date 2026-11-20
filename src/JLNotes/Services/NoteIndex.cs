using System.IO;
using JLNotes.Models;

namespace JLNotes.Services;

/// <summary>
/// The one answer to "which note does this name mean", for links between notes and
/// for the jln command alike, and the one record of who links to whom. A name is a
/// note's file name (with or without ".md" or a full path), or that file name without
/// its date when only one note has it. Titles are never used: two notes can share
/// one, and a title can change while a file name never does.
/// </summary>
public sealed class NoteIndex
{
    private Dictionary<string, Note> _byFileName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<Note>> _bySlug = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Note, List<Note>> _linkedFrom = [];

    /// <summary>A link was clicked; the main view brings this note up.</summary>
    public event Action<Note>? OpenRequested;

    public void Refresh(IReadOnlyList<Note> notes)
    {
        _byFileName = notes
            .GroupBy(n => Path.GetFileNameWithoutExtension(n.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _bySlug = notes
            .GroupBy(n => n.GetSlug(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        _linkedFrom = [];
        foreach (var from in notes)
        {
            foreach (var to in from.GetLinkTargets().Select(Resolve).OfType<Note>().Distinct())
            {
                if (to == from) continue;
                if (!_linkedFrom.TryGetValue(to, out var list))
                    _linkedFrom[to] = list = [];
                list.Add(from);
            }
        }
    }

    /// <summary>The note this name means, or nothing if none or several match.</summary>
    public Note? Resolve(string name) => Candidates(name) is [var only] ? only : null;

    /// <summary>Every note this name could mean: one for a full file name, and for a
    /// file name without its date, each note that has it.</summary>
    public IReadOnlyList<Note> Candidates(string name)
    {
        var key = Path.GetFileName(name.Trim());
        if (key.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            key = key[..^3];
        if (_byFileName.TryGetValue(key, out var note))
            return [note];
        return _bySlug.TryGetValue(key, out var notes) ? notes : [];
    }

    /// <summary>Every other note with a link to this one, in the order they were given.</summary>
    public IReadOnlyList<Note> LinkedFrom(Note note) =>
        _linkedFrom.TryGetValue(note, out var list) ? list : [];

    public void RequestOpen(string name)
    {
        if (Resolve(name) is { } note)
            OpenRequested?.Invoke(note);
    }
}
