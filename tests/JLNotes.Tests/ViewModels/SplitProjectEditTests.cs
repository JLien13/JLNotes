using System.IO;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// Project edited in the split detail pane rides EditProject (shared with the
// list card) and CommitSplitEdit (shared with title/body/tags).
public class SplitProjectEditTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public SplitProjectEditTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-splitproject-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDir, "notes"));
        _noteService = new NoteService(Path.Combine(_testDir, "notes"));
        _settingsService = new SettingsService(_testDir);
        _projectService = new ProjectService(_testDir);
        _projectService.Save([new ProjectInfo { Name = "CorVascular" }, new ProjectInfo { Name = "A1i" }]);
    }

    public void Dispose()
    {
        _noteService.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private NoteItemViewModel SavedVm(string project)
    {
        var note = new Note { Title = "T", Body = "b", Project = project, Created = new DateTime(2026, 9, 14) };
        _noteService.Save(note);
        return new NoteItemViewModel(note, _noteService, _settingsService, _projectService);
    }

    [Fact]
    public void Commit_AfterProjectEdit_PersistsToDisk()
    {
        var vm = SavedVm("");
        vm.BeginSplitEdit();

        vm.EditProject = "A1i";
        vm.CommitSplitEdit();

        Assert.Equal("A1i", vm.Note.Project);
        var reloaded = Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath);
        Assert.Equal("A1i", reloaded.Project);
    }

    [Fact]
    public void SplitProjectChoice_MapsNoProjectToBlank_AndSavesImmediately()
    {
        var vm = SavedVm("CorVascular");
        vm.BeginSplitEdit();
        Assert.Equal("CorVascular", vm.SplitProjectChoice);

        vm.SplitProjectChoice = NoteItemViewModel.NoProject;

        Assert.Equal("", vm.Note.Project);
        Assert.Equal(NoteItemViewModel.NoProject, vm.SplitProjectChoice);
        var reloaded = Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath);
        Assert.Equal("", reloaded.Project);
    }

    [Fact]
    public void ProjectChoices_StartWithNoProject_AndIncludeAnUnregisteredCurrentProject()
    {
        var vm = SavedVm("Skunkworks");

        var choices = vm.ProjectChoices;

        Assert.Equal(NoteItemViewModel.NoProject, choices[0]);
        Assert.Contains("CorVascular", choices);
        Assert.Contains("A1i", choices);
        Assert.Contains("Skunkworks", choices);
    }
}
