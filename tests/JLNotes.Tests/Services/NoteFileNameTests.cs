using System.IO;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// A new note's file name comes from its title + created date. Two new notes
// with the same title on the same day must never share a file: the second
// save used to silently overwrite the first.
public class NoteFileNameTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _service;

    public NoteFileNameTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-filename-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _service = new NoteService(_testDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void Save_SameTitleSameDay_GetsDistinctFiles()
    {
        var day = new DateTime(2026, 9, 14, 13, 0, 0);
        var a = new Note { Title = "Same title", Created = day };
        var b = new Note { Title = "Same title", Created = day };
        var c = new Note { Title = "Same title", Created = day };

        _service.Save(a);
        _service.Save(b);
        _service.Save(c);

        Assert.NotEqual(a.FilePath, b.FilePath);
        Assert.NotEqual(b.FilePath, c.FilePath);
        Assert.Equal(3, _service.LoadAll().Count);
    }

    [Fact]
    public void Save_ExistingNote_KeepsItsFile()
    {
        var note = new Note { Title = "Keep me", Created = new DateTime(2026, 9, 14) };
        _service.Save(note);
        var first = note.FilePath;

        note.Body = "edited";
        _service.Save(note);

        Assert.Equal(first, note.FilePath);
        Assert.Single(_service.LoadAll());
    }
}
