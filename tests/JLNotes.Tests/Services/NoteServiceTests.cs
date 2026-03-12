using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

public class NoteServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _service;

    public NoteServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"claude-notes-test-{Guid.NewGuid()}");
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
    public void LoadAll_ReturnsEmptyForEmptyDir()
    {
        var notes = _service.LoadAll();
        Assert.Empty(notes);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var note = new Note
        {
            Title = "Test note",
            Project = "TestProject",
            Priority = NotePriority.High,
            Created = new DateTime(2026, 3, 11)
        };
        _service.Save(note);
        var loaded = _service.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("Test note", loaded[0].Title);
        Assert.Equal(NotePriority.High, loaded[0].Priority);
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var note = new Note
        {
            Title = "To delete",
            Created = new DateTime(2026, 3, 11)
        };
        _service.Save(note);
        Assert.Single(_service.LoadAll());
        _service.Delete(note);
        Assert.Empty(_service.LoadAll());
    }

    [Fact]
    public void FileWatcher_RaisesEvent_WhenFileCreated()
    {
        var raised = false;
        _service.NotesChanged += () => raised = true;
        _service.StartWatching();

        var path = Path.Combine(_testDir, "new-note.md");
        File.WriteAllText(path, """
            ---
            title: External note
            project: Test
            priority: low
            status: open
            tags: []
            created: 2026-03-11T00:00:00
            updated: 2026-03-11T00:00:00
            ---

            Body here.
            """);

        // Give FileSystemWatcher time to fire
        Thread.Sleep(500);
        Assert.True(raised);
    }
}
