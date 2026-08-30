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
    private string _statusFilter = "all";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _groupByProject;

    [ObservableProperty]
    private bool _sortByDate;

    // Layout mode for the note area: "list" | "grid" | "split". Chosen from the
    // header View dropdown.
    [ObservableProperty]
    private string _viewMode = DefaultViewMode;

    /// <summary>The view a brand-new install opens in — single source of truth
    /// for the first-run default (see <see cref="ResolveInitialViewMode"/>).</summary>
    public const string DefaultViewMode = "split";

    /// <summary>Resolve which view mode to start in, in priority order:
    /// (1) an explicit prior choice — "remember the last selected view, always";
    /// (2) the legacy <c>gridView</c> flag — migrate pre-viewMode users;
    /// (3) a brand-new install (no settings file yet) — the split default;
    /// (4) otherwise an existing pre-viewMode user — keep the historical list.</summary>
    public static string ResolveInitialViewMode(AppSettings settings, bool settingsFileExisted)
    {
        if (!string.IsNullOrEmpty(settings.ViewMode))
            return settings.ViewMode;
        if (settings.GridView)
            return "grid";
        return settingsFileExisted ? "list" : DefaultViewMode;
    }

    [ObservableProperty]
    private bool _isSelectMode;

    // Split (master-detail) view: the note selected in the left rail, shown in
    // the right reading/edit pane.
    [ObservableProperty]
    private NoteItemViewModel? _selectedSplitNote;

    public ObservableCollection<NoteItemViewModel> HighPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> MediumPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> LowPriority { get; } = [];
    public ObservableCollection<NoteItemViewModel> SortedByDate { get; } = [];
    public ObservableCollection<ProjectGroupViewModel> ProjectGroups { get; } = [];
    // Flat, filtered note list backing the split view's left rail (virtualized).
    public ObservableCollection<NoteItemViewModel> SplitNotes { get; } = [];
    public ObservableCollection<string> Projects { get; } = ["Projects"];

    // Header View dropdown.
    public string[] ViewModes { get; } = ["List", "Grid", "Split"];

    public string SelectedViewMode
    {
        get => ViewMode switch { "grid" => "Grid", "split" => "Split", _ => "List" };
        set => ViewMode = (value ?? "").ToLowerInvariant() switch
        {
            "grid" => "grid",
            "split" => "split",
            _ => "list"
        };
    }

    public bool IsGridLayout => ViewMode == "grid";
    public bool IsSplitLayout => ViewMode == "split";

    /// <summary>Header version label, e.g. "v1.2.0" — read from the assembly so it
    /// always tracks the csproj &lt;Version&gt; (single source of truth).</summary>
    public string AppVersion { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "";

    public bool ShowDateView => SortByDate;
    public bool ShowProjectView => !SortByDate && GroupByProject;
    public bool ShowPriorityView => !SortByDate && !GroupByProject;

    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    public string StatusFilterIcon => StatusFilter switch
    {
        "open" => "○",  // ○
        "done" => "●",  // ●
        _ => "◐"        // ◐ = all
    };

    public string StatusFilterTooltip => StatusFilter switch
    {
        "open" => "Showing: Open only — click for Done only",
        "done" => "Showing: Done only — click for All",
        _ => "Showing: All — click for Open only"
    };

    private int _filteredCount;
    public bool IsEmpty => _filteredCount == 0;
    public string EmptyMessage =>
        HasSearchText ? $"No notes match “{SearchText}”"
        : _cachedNotes.Count == 0 ? "No notes yet — hit + to add one"
        : "Nothing here — try toggling filters";

    public NoteService NoteService => _noteService;
    public ProjectService ProjectService => _projectService;
    public SettingsService SettingsService => _settingsService;

    private IEnumerable<NoteItemViewModel> AllNoteViewModels =>
        SortByDate
            ? SortedByDate
            : GroupByProject
                ? ProjectGroups.SelectMany(pg => pg.Notes)
                : HighPriority.Concat(MediumPriority).Concat(LowPriority);

    public int SelectedCount => AllNoteViewModels.Count(vm => vm.IsSelected);

    public MainViewModel(NoteService noteService, ProjectService projectService, SettingsService settingsService)
    {
        _noteService = noteService;
        _projectService = projectService;
        _settingsService = settingsService;

        var settingsExisted = settingsService.Exists;
        var settings = settingsService.Load();
        _statusFilter = NormalizeStatusFilter(settings.StatusFilter);
        _groupByProject = settings.GroupByProject;
        _sortByDate = settings.SortByDate;
        _viewMode = ResolveInitialViewMode(settings, settingsExisted);

        // First run: persist the resolved default so the chosen view is
        // remembered like any later selection — otherwise an unrelated
        // load-modify-save (e.g. toggling a filter) would clobber it back.
        if (!settingsExisted)
        {
            settings.ViewMode = _viewMode;
            settings.GridView = _viewMode == "grid";
            settingsService.Save(settings);
        }

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
            StatusFilter switch
            {
                "open" => n.Status != NoteStatus.Done,
                "done" => n.Status == NoteStatus.Done,
                _ => true
            } &&
            (string.IsNullOrEmpty(SearchText) ||
             n.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             n.Body.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             n.Tags.Any(t => t.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
        ).ToList();

        RebuildGroup(HighPriority, filtered.Where(n => n.Priority == NotePriority.High));
        RebuildGroup(MediumPriority, filtered.Where(n => n.Priority == NotePriority.Medium));
        RebuildGroup(LowPriority, filtered.Where(n => n.Priority == NotePriority.Low));
        RebuildGroup(SortedByDate, filtered.OrderByDescending(n => n.Created));
        RebuildProjectGroups(filtered);
        RebuildSplit(filtered);

        _filteredCount = filtered.Count;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
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

    private void RebuildSplit(List<Note> filtered)
    {
        // Flush any in-place detail edits before we swap out the VM instances,
        // so a background refresh (file watcher, filter/search change) never
        // drops unsaved work. No-ops when nothing changed.
        SelectedSplitNote?.CommitSplitEdit();

        var ordered = SortByDate
            ? filtered.OrderByDescending(n => n.Created)
            : filtered
                .OrderBy(n => n.Priority switch
                {
                    NotePriority.High => 0,
                    NotePriority.Medium => 1,
                    NotePriority.Low => 2,
                    _ => 3
                })
                .ThenBy(n => n.SortOrder)
                .ThenByDescending(n => n.Created);

        var previousPath = SelectedSplitNote?.Note.FilePath;

        SplitNotes.Clear();
        foreach (var note in ordered)
        {
            var vm = new NoteItemViewModel(note, _noteService, _settingsService, _projectService);
            vm.NoteChanged += () => RefreshNotes();
            vm.NoteDeleted += () => RefreshNotes();
            SplitNotes.Add(vm);
        }

        // Preserve the prior selection across the rebuild; otherwise default to the
        // first note so the detail pane is never blank when notes exist.
        SelectedSplitNote =
            (previousPath != null
                ? SplitNotes.FirstOrDefault(v => v.Note.FilePath == previousPath)
                : null)
            ?? SplitNotes.FirstOrDefault();
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
    partial void OnStatusFilterChanged(string value)
    {
        var settings = _settingsService.Load();
        settings.StatusFilter = value;
        _settingsService.Save(settings);
        OnPropertyChanged(nameof(StatusFilterIcon));
        OnPropertyChanged(nameof(StatusFilterTooltip));
        RefreshNotes();
    }

    [RelayCommand]
    private void CycleStatusFilter()
    {
        StatusFilter = StatusFilter switch
        {
            "all" => "open",
            "open" => "done",
            _ => "all"
        };
    }

    private static string NormalizeStatusFilter(string? value) =>
        value is "open" or "done" or "all" ? value : "all";
    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        _searchDebounce!.Stop();
        _searchDebounce.Start();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";
    partial void OnGroupByProjectChanged(bool value)
    {
        var settings = _settingsService.Load();
        settings.GroupByProject = value;
        _settingsService.Save(settings);
        OnPropertyChanged(nameof(ShowProjectView));
        OnPropertyChanged(nameof(ShowPriorityView));
        RefreshNotes();
    }
    partial void OnSortByDateChanged(bool value)
    {
        var settings = _settingsService.Load();
        settings.SortByDate = value;
        _settingsService.Save(settings);
        OnPropertyChanged(nameof(ShowDateView));
        OnPropertyChanged(nameof(ShowProjectView));
        OnPropertyChanged(nameof(ShowPriorityView));
        RefreshNotes();
    }

    partial void OnViewModeChanged(string? oldValue, string newValue)
    {
        // Leaving split: flush the in-place detail editor, then rebuild the other
        // views from the (now-current) in-memory cache so they reflect the edit.
        // This is off the hot path — a deliberate layout switch, not note clicks —
        // and avoids a disk reload since the save no longer trips the watcher.
        if (oldValue == "split")
        {
            SelectedSplitNote?.CommitSplitEdit();
            RefreshFromCache();
        }

        var settings = _settingsService.Load();
        settings.ViewMode = newValue;
        settings.GridView = newValue == "grid"; // keep legacy flag in sync
        _settingsService.Save(settings);
        OnPropertyChanged(nameof(IsGridLayout));
        OnPropertyChanged(nameof(IsSplitLayout));
        OnPropertyChanged(nameof(SelectedViewMode));

        // Entering split: ensure a note is selected and primed for editing.
        if (newValue == "split")
        {
            if (SelectedSplitNote == null)
                SelectedSplitNote = SplitNotes.FirstOrDefault(); // OnSelectedSplitNoteChanged primes it
            else
                SelectedSplitNote.BeginSplitEdit();
        }
    }

    // Split detail pane is always editable: commit the note we're leaving and
    // prime the one we're entering so its title/body load into the editor.
    partial void OnSelectedSplitNoteChanged(NoteItemViewModel? oldValue, NoteItemViewModel? newValue)
    {
        oldValue?.CommitSplitEdit();
        newValue?.BeginSplitEdit();
    }

    /// <summary>Flush the split detail pane's in-place edits to disk. Safe to
    /// call anytime — no-ops when nothing changed. Used by window blur/close.</summary>
    public void CommitSplitEdit() => SelectedSplitNote?.CommitSplitEdit();

    partial void OnIsSelectModeChanged(bool value)
    {
        foreach (var vm in HighPriority.Concat(MediumPriority).Concat(LowPriority).Concat(SortedByDate))
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
            ExportService.ExportToWord(selected, dialog.FileName, _noteService.GetAttachmentsDir);
            IsSelectMode = false;
        }
    }
}
