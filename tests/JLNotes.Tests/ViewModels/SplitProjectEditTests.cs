using System.IO;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// Project edited in the split detail pane rides EditProject (shared with the
// list card) and SaveEditor (shared with title/body/tags).
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
        return new NoteItemViewModel(note, _noteService, _settingsService, new ProjectCatalog());
    }

    private MainViewModel SplitView()
    {
        var vm = new MainViewModel(_noteService, _projectService, _settingsService);
        vm.ViewMode = "split";
        return vm;
    }

    [Fact]
    public void Commit_AfterProjectEdit_PersistsToDisk()
    {
        var vm = SavedVm("");
        vm.LoadEditor();

        vm.EditProject = "A1i";
        vm.SaveEditor();

        Assert.Equal("A1i", vm.Note.Project);
        var reloaded = Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath);
        Assert.Equal("A1i", reloaded.Project);
    }

    [Fact]
    public void SplitProjectChoice_MapsNoProjectToBlank_AndSavesImmediately()
    {
        var vm = SavedVm("CorVascular");
        vm.LoadEditor();
        Assert.Equal("CorVascular", vm.SplitProjectChoice);

        vm.SplitProjectChoice = ProjectCatalog.NoProject;

        Assert.Equal("", vm.Note.Project);
        Assert.Equal(ProjectCatalog.NoProject, vm.SplitProjectChoice);
        var reloaded = Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath);
        Assert.Equal("", reloaded.Project);
    }

    [Fact]
    public void Picker_ShowsTheSameProjectsAsTheHeader()
    {
        SavedVm("Skunkworks");
        var vm = SplitView();

        Assert.Equal(["A1i", "CorVascular", "Skunkworks"], vm.Projects.Skip(1));
        Assert.Equal(ProjectCatalog.NoProject, vm.SelectedSplitNote!.ProjectChoices[0]);
        Assert.Equal(vm.Projects.Skip(1), vm.SelectedSplitNote.ProjectChoices.Skip(1));
    }

    [Fact]
    public void ProjectNames_DifferingOnlyByCase_AreOneProject()
    {
        SavedVm("VG-Fleet");
        SavedVm("vg-fleet");
        var vm = SplitView();

        Assert.Single(vm.Projects, p => ProjectCatalog.Same(p, "vg-fleet"));

        vm.SelectedProject = "VG-Fleet";
        Assert.Equal(2, vm.SplitNotes.Count);

        // A note spelled the other way still shows as selected in the picker, and
        // merely showing it does not rewrite its file.
        var lowerCase = vm.SplitNotes.Single(n => n.Project == "vg-fleet");
        lowerCase.LoadEditor();
        Assert.Equal("VG-Fleet", lowerCase.SplitProjectChoice);
        lowerCase.SplitProjectChoice = "VG-Fleet";
        Assert.Equal("vg-fleet", lowerCase.Note.Project);
    }
}
