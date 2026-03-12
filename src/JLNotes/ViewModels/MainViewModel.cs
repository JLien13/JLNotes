using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly NoteService _noteService;
    private readonly ProjectService _projectService;
    private readonly SettingsService _settingsService;

    [ObservableProperty]
    private string _selectedProject = "Projects";

    [ObservableProperty]
    private bool _showCompleted;

    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<NoteItemViewModel> HighPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> MediumPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> LowPriority { get; } = [];
    public ObservableCollection<string> Projects { get; } = ["Projects"];

    public NoteService NoteService => _noteService;
    public ProjectService ProjectService => _projectService;
    public SettingsService SettingsService => _settingsService;

    public MainViewModel(NoteService noteService, ProjectService projectService, SettingsService settingsService)
    {
        _noteService = noteService;
        _projectService = projectService;
        _settingsService = settingsService;

        LoadProjects();
        RefreshNotes();

        _noteService.NotesChanged += () =>
            System.Windows.Application.Current?.Dispatcher.Invoke(RefreshNotes);
        _noteService.StartWatching();
    }

    private void LoadProjects(IEnumerable<Note>? notes = null)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Projects" };

        // From projects.json
        foreach (var p in _projectService.Load())
            known.Add(p.Name);

        // From note frontmatter
        if (notes != null)
            foreach (var n in notes)
                if (!string.IsNullOrWhiteSpace(n.Project))
                    known.Add(n.Project);

        // Sync the collection (keep "Projects" first)
        var sorted = known.Where(k => k != "Projects").OrderBy(k => k).ToList();
        Projects.Clear();
        Projects.Add("Projects");
        foreach (var name in sorted)
            Projects.Add(name);
    }

    public void RefreshNotes()
    {
        var all = _noteService.LoadAll();
        LoadProjects(all);

        var filtered = all.Where(n =>
            (SelectedProject == "Projects" || n.Project == SelectedProject) &&
            (ShowCompleted || n.Status != NoteStatus.Done) &&
            (string.IsNullOrEmpty(SearchText) ||
             n.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             n.Body.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             n.Tags.Any(t => t.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
        ).ToList();

        RebuildGroup(HighPriority, filtered.Where(n => n.Priority == NotePriority.High));
        RebuildGroup(MediumPriority, filtered.Where(n => n.Priority == NotePriority.Medium));
        RebuildGroup(LowPriority, filtered.Where(n => n.Priority == NotePriority.Low));
    }

    private void RebuildGroup(ObservableCollection<NoteItemViewModel> group, IEnumerable<Note> notes)
    {
        group.Clear();
        foreach (var note in notes)
        {
            var vm = new NoteItemViewModel(note, _noteService, _settingsService, _projectService);
            vm.NoteChanged += () => RefreshNotes();
            vm.NoteDeleted += () => RefreshNotes();
            vm.DropReceived += HandleReorder;
            group.Add(vm);
        }
    }

    private void HandleReorder(NoteItemViewModel source, NoteItemViewModel target)
    {
        if (source.Priority != target.Priority) return;

        var group = GetGroupForPriority(source.Priority);
        if (group == null) return;

        var sourceIndex = group.IndexOf(source);
        var targetIndex = group.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;

        group.Move(sourceIndex, targetIndex);

        for (int i = 0; i < group.Count; i++)
        {
            group[i].Note.SortOrder = i;
            _noteService.Save(group[i].Note);
        }
    }

    private ObservableCollection<NoteItemViewModel>? GetGroupForPriority(NotePriority priority) => priority switch
    {
        NotePriority.High => HighPriority,
        NotePriority.Medium => MediumPriority,
        NotePriority.Low => LowPriority,
        _ => null
    };

    public void CollapseAll()
    {
        foreach (var vm in HighPriority.Concat(MediumPriority).Concat(LowPriority))
            if (vm.IsExpanded) vm.CancelEditsCommand.Execute(null);
    }

    public void SaveExpanded()
    {
        foreach (var vm in HighPriority.Concat(MediumPriority).Concat(LowPriority))
            if (vm.IsExpanded) vm.SaveEditsCommand.Execute(null);
    }

    partial void OnSelectedProjectChanged(string value) => RefreshNotes();
    partial void OnShowCompletedChanged(bool value) => RefreshNotes();
    partial void OnSearchTextChanged(string value) => RefreshNotes();
}
