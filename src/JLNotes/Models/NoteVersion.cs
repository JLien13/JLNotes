using System.IO;

namespace JLNotes.Models;

/// <summary>
/// A note file exactly as it was before something overwrote or removed it, kept in
/// the history folder. <paramref name="NotePath"/> is where the note lives (or lived);
/// <paramref name="SavedAt"/> is when this text was replaced.
/// </summary>
public sealed record NoteVersion(string NotePath, string VersionPath, DateTime SavedAt)
{
    public Note Read() => Note.ParseFromMarkdown(File.ReadAllText(VersionPath), NotePath);
}
