using System.IO;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// Tags edited in the split detail pane go through the same BeginSplitEdit /
// CommitSplitEdit path as the title and body, and the same comma-separated
// parser the list card uses.
public class SplitTagEditTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public SplitTagEditTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-splittags-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDir, "notes"));
        _noteService = new NoteService(Path.Combine(_testDir, "notes"));
        _settingsService = new SettingsService(_testDir);
        _projectService = new ProjectService(_testDir);
    }

    public void Dispose()
    {
        _noteService.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private NoteItemViewModel SavedVm(params string[] tags)
    {
        var note = new Note { Title = "Tagged", Body = "body", Tags = [.. tags], Created = new DateTime(2026, 9, 14) };
        _noteService.Save(note);
        return new NoteItemViewModel(note, _noteService, _settingsService, _projectService);
    }

    [Fact]
    public void Commit_AfterTagEdit_PersistsToDisk()
    {
        var vm = SavedVm("competitor");
        vm.BeginSplitEdit();

        vm.EditTags = "competitor, hokanson, intel";
        vm.CommitSplitEdit();

        Assert.Equal(["competitor", "hokanson", "intel"], vm.Note.Tags);
        var reloaded = Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath);
        Assert.Equal(["competitor", "hokanson", "intel"], reloaded.Tags);
    }

    [Fact]
    public void Commit_RemovingAllTags_PersistsEmptyList()
    {
        var vm = SavedVm("a", "b");
        vm.BeginSplitEdit();

        vm.EditTags = "";
        vm.CommitSplitEdit();

        Assert.Empty(vm.Note.Tags);
        Assert.False(vm.HasTags);
    }

    [Fact]
    public void Commit_WithSameTagsRetyped_DoesNotRewriteFile()
    {
        var vm = SavedVm("a", "b");
        vm.BeginSplitEdit();
        var before = File.GetLastWriteTimeUtc(vm.Note.FilePath);

        vm.EditTags = " a ,b, ";
        vm.CommitSplitEdit();

        Assert.Equal(before, File.GetLastWriteTimeUtc(vm.Note.FilePath));
    }

    [Theory]
    [InlineData("a, b, c", new[] { "a", "b", "c" })]
    [InlineData("  a ,b ,, c  ", new[] { "a", "b", "c" })]
    [InlineData("a, a, A", new[] { "a", "A" })]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    public void ParseTags_IsTheOneTagParser(string text, string[] expected)
    {
        Assert.Equal(expected, NoteItemViewModel.ParseTags(text));
    }
}
