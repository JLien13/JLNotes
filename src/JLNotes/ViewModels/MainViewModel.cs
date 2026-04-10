using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
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
    private DispatcherTimer? _searchDebounce;
    private List<Note> _cachedNotes = [];
    private List<ProjectInfo> _cachedProjects = [];

    [ObservableProperty]
    private string _selectedProject = "Projects";

    [ObservableProperty]
    private bool _showCompleted;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _groupByProject;

    [ObservableProperty]
    private bool _isSelectMode;

    public ObservableCollection<NoteItemViewModel> HighPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> MediumPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> LowPriority { get; } = [];
    public ObservableCollection<ProjectGroupViewModel> ProjectGroups { get; } = [];
    public ObservableCollection<string> Projects { get; } = ["Projects"];

    public NoteService NoteService => _noteService;
    public ProjectService ProjectService => _projectService;
    public SettingsService SettingsService => _settingsService;

    private IEnumerable<NoteItemViewModel> AllNoteViewModels =>
        GroupByProject
            ? ProjectGroups.SelectMany(pg => pg.Notes)
            : HighPriority.Concat(MediumPriority).Concat(LowPriority);

    public int SelectedCount => AllNoteViewModels.Count(vm => vm.IsSelected);

    public MainViewModel(NoteService noteService, ProjectService projectService, SettingsService settingsService)
    {
        _noteService = noteService;
        _projectService = projectService;
        _settingsService = settingsService;

        var settings = settingsService.Load();
        _showCompleted = settings.ShowCompleted;
        _groupByProject = settings.GroupByProject;
        RefreshNotes();

        _noteService.NotesChanged += () =>
            System.Windows.Application.Current?.Dispatcher.Invoke(RefreshNotes);
        _noteService.StartWatching();

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RefreshFromCache();
        };
    }

    private void LoadProjects(IEnumerable<Note>? notes = null)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Projects" };

        // From projects.json (cached)
        foreach (var p in _cachedProjects)
            known.Add(p.Name);

        // From note frontmatter
        if (notes != null)
            foreach (var n in notes)
                if (!string.IsNullOrWhiteSpace(n.Project))
                    known.Add(n.Project);

        // Sync the collection (keep "Projects" first), but only if changed
        var sorted = known.Where(k => k != "Projects").OrderBy(k => k).ToList();
        var desired = new List<string> { "Projects" };
        desired.AddRange(sorted);

        if (Projects.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase))
            return;

        Projects.Clear();
        Projects.Add("Projects");
        foreach (var name in sorted)
            Projects.Add(name);
    }

    public void RefreshNotes()
    {
        _cachedNotes = _noteService.LoadAll();
        _cachedProjects = _projectService.Load();
        LoadProjects(_cachedNotes);
        RefreshFromCache();
    }

    private void RefreshFromCache()
    {
        var filtered = _cachedNotes.Where(n =>
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
        RebuildProjectGroups(filtered);
    }

    private void RebuildProjectGroups(List<Note> filtered)
    {
        ProjectGroups.Clear();
        if (!GroupByProject) return;

        var projectInfos = _cachedProjects;

        // Group notes by project name
        var groups = filtered
            .GroupBy(n => string.IsNullOrWhiteSpace(n.Project) ? "(No Project)" : n.Project,
                     StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            var info = projectInfos.FirstOrDefault(
                p => p.Name.Equals(g.Key, StringComparison.OrdinalIgnoreCase));

            Brush colorBrush = Brushes.Gray;
            if (info != null && !string.IsNullOrEmpty(info.Color))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(info.Color);
                    colorBrush = new SolidColorBrush(color);
                }
                catch { }
            }

            var pg = new ProjectGroupViewModel
            {
                Name = g.Key,
                ColorBrush = colorBrush
            };

            foreach (var note in g)
            {
                var vm = new NoteItemViewModel(note, _noteService, _settingsService, _projectService);
                vm.NoteChanged += () => RefreshNotes();
                vm.NoteDeleted += () => RefreshNotes();
                vm.DropReceived += HandleReorder;
                vm.IsSelectMode = IsSelectMode;
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(NoteItemViewModel.IsSelected))
                        OnPropertyChanged(nameof(SelectedCount));
                };
                pg.Notes.Add(vm);
            }

            ProjectGroups.Add(pg);
        }
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
            vm.IsSelectMode = IsSelectMode;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NoteItemViewModel.IsSelected))
                    OnPropertyChanged(nameof(SelectedCount));
            };
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
        foreach (var vm in AllNoteViewModels)
            if (vm.IsExpanded) vm.CancelEditsCommand.Execute(null);
    }

    public void SaveExpanded()
    {
        foreach (var vm in AllNoteViewModels)
            if (vm.IsExpanded) vm.SaveEditsCommand.Execute(null);
    }

    partial void OnSelectedProjectChanged(string value) => RefreshNotes();
    partial void OnShowCompletedChanged(bool value)
    {
        var settings = _settingsService.Load();
        settings.ShowCompleted = value;
        _settingsService.Save(settings);
        RefreshNotes();
    }
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce!.Stop();
        _searchDebounce.Start();
    }
    partial void OnGroupByProjectChanged(bool value)
    {
        var settings = _settingsService.Load();
        settings.GroupByProject = value;
        _settingsService.Save(settings);
        RefreshNotes();
    }

    partial void OnIsSelectModeChanged(bool value)
    {
        foreach (var vm in HighPriority.Concat(MediumPriority).Concat(LowPriority))
        {
            vm.IsSelectMode = value;
            if (!value) vm.IsSelected = false;
        }
        foreach (var pg in ProjectGroups)
            foreach (var vm in pg.Notes)
            {
                vm.IsSelectMode = value;
                if (!value) vm.IsSelected = false;
            }
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void ExportSelected()
    {
        var selected = AllNoteViewModels.Where(vm => vm.IsSelected).Select(vm => vm.Note).ToList();
        if (selected.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Selected Notes to Word",
            Filter = "Word Document|*.docx",
            FileName = selected.Count == 1
                ? $"{selected[0].GetSlug()}.docx"
                : $"{selected[0].GetSlug()}-and-{selected.Count - 1}-more.docx"
        };
        if (dialog.ShowDialog() == true)
        {
            ExportService.ExportToWord(selected, dialog.FileName);
            IsSelectMode = false;
        }
    }
}
