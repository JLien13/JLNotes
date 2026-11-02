using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLNotes.Helpers;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly NoteService _noteService;
    private readonly ProjectService _projectService;
    private readonly SettingsService _settingsService;
    private readonly ProjectCatalog _projects = new();
    private DispatcherTimer? _searchDebounce;

    // The one list of notes: one view model per note, for as long as its file
    // exists. The collections below are filtered and sorted windows onto it.
    private List<NoteItemViewModel> _items = [];

    // Which note the split pane comes back to after a visit to another layout.
    private string? _splitSelectionPath;

    [ObservableProperty]
    private string _selectedProject = ProjectCatalog.AllProjects;

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
    public ObservableCollection<string> Projects => _projects.FilterChoices;

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

    /// <summary>Set by App's quiet update check once a newer release is found
    /// (e.g. "1.2.7"); null = none known. Drives the header "Update" link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateLinkText))]
    private string? _updateAvailableVersion;

    /// <summary>Progress / result text while an update run is in flight; null = idle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateLinkText))]
    private string? _updateStatus;

    public bool HasUpdate => UpdateAvailableVersion != null;
    public string UpdateLinkText => UpdateStatus ?? (UpdateAvailableVersion is { } v ? $"Update to {v}" : "");

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
        : _items.Count == 0 ? "No notes yet. Hit + to add one."
        : "Nothing here — try toggling filters";

    /// <summary>Shown under the search bar when a note file could not be read at
    /// all; null hides it. A note should never just go missing without a word.</summary>
    public string? UnreadableNotesMessage => _noteService.UnreadableFiles.Count switch
    {
        0 => null,
        1 => "1 note could not be read",
        var count => $"{count} notes could not be read"
    };

    /// <summary>Names the unreadable files, for the message's tooltip.</summary>
    public string UnreadableNotesDetail => string.Join(Environment.NewLine, _noteService.UnreadableFiles);

    public NoteService NoteService => _noteService;
    public ProjectService ProjectService => _projectService;
    public SettingsService SettingsService => _settingsService;

    private IEnumerable<NoteItemViewModel> OnScreen =>
        IsSplitLayout
            ? SplitNotes
            : SortByDate
                ? SortedByDate
                : GroupByProject
                    ? ProjectGroups.SelectMany(pg => pg.Notes)
                    : HighPriority.Concat(MediumPriority).Concat(LowPriority);

    public int SelectedCount => OnScreen.Count(vm => vm.IsSelected);

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
            UpdateWindows();
        };
    }

    /// <summary>Reads the notes folder again and brings the one list up to date.
    /// A note that is still there keeps its view model, so its row, selection and
    /// open editor all survive.</summary>
    public void RefreshNotes()
    {
        var notes = _noteService.LoadAll();
        _projects.Refresh(_projectService.Load(), notes, SelectedProject);
        var subtitleDisplay = _settingsService.Load().SubtitleDisplay;

        var previous = _items.ToDictionary(vm => vm.Note.FilePath, StringComparer.OrdinalIgnoreCase);
        _items = notes.Select(note =>
        {
            if (!previous.Remove(note.FilePath, out var vm))
                return CreateItem(note);
            vm.Reload(note);
            return vm;
        }).ToList();
        foreach (var vm in _items)
            vm.SubtitleDisplay = subtitleDisplay;

        // Their files are gone, and saving their editors would bring them back.
        foreach (var gone in previous.Values)
            gone.CancelEditsCommand.Execute(null);

        OnPropertyChanged(nameof(UnreadableNotesMessage));
        OnPropertyChanged(nameof(UnreadableNotesDetail));
        UpdateWindows();
    }

    private NoteItemViewModel CreateItem(Note note)
    {
        var vm = new NoteItemViewModel(note, _noteService, _settingsService, _projects) { IsSelectMode = IsSelectMode };
        vm.NoteChanged += RefreshNotes;
        vm.NoteDeleted += RefreshNotes;
        vm.DropReceived += HandleReorder;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteItemViewModel.IsSelected))
                OnPropertyChanged(nameof(SelectedCount));
        };
        return vm;
    }

    /// <summary>Tag chip click: require this tag in the search (see NoteSearch).</summary>
    public void AddTagToSearch(string tag) => SearchText = Helpers.NoteSearch.AddTag(SearchText, tag);

    private void UpdateWindows()
    {
        var query = Helpers.NoteSearch.Parse(SearchText);
        var filtered = _items.Where(vm =>
            ProjectCatalog.PassesFilter(vm.Project, SelectedProject) &&
            StatusFilter switch
            {
                "open" => vm.Note.Status != NoteStatus.Done,
                "done" => vm.Note.Status == NoteStatus.Done,
                _ => true
            } &&
            Helpers.NoteSearch.Matches(vm.Note, query)
        ).ToList();

        var byDate = filtered.OrderByDescending(vm => vm.Created).ToList();
        SyncWindow(SortedByDate, byDate);
        SyncWindow(HighPriority, filtered.Where(vm => vm.Priority == NotePriority.High).ToList());
        SyncWindow(MediumPriority, filtered.Where(vm => vm.Priority == NotePriority.Medium).ToList());
        SyncWindow(LowPriority, filtered.Where(vm => vm.Priority == NotePriority.Low).ToList());
        SyncProjectGroups(GroupByProject ? filtered : []);
        SyncWindow(SplitNotes, SortByDate
            ? byDate
            : filtered.OrderByDescending(vm => vm.Priority).ThenBy(vm => vm.Note.SortOrder).ThenByDescending(vm => vm.Created).ToList());

        // The detail pane is bound to this note even while hidden, so it must let go
        // outside split view or a list card opening the same note would need the
        // same document. In split, keep the selected note if it is still listed,
        // otherwise fall back to the first so the pane is never blank.
        SelectedSplitNote = !IsSplitLayout
            ? null
            : SplitNotes.FirstOrDefault(vm => vm.Note.FilePath == _splitSelectionPath) ?? SplitNotes.FirstOrDefault();

        _filteredCount = filtered.Count;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(SelectedCount));
    }

    // A list card's editor lives in its row and cannot follow the note to another
    // one, so a note leaving a window saves and closes its card first.
    private static void SyncWindow(ObservableCollection<NoteItemViewModel> window, List<NoteItemViewModel> wanted)
    {
        foreach (var leaving in window.Except(wanted))
            if (leaving.IsExpanded) leaving.CloseEditor();
        window.SyncTo(wanted);
    }

    // Changing view brings up different rows, and an open card cannot move to them.
    private void CloseCards()
    {
        foreach (var vm in _items)
            if (vm.IsExpanded) vm.CloseEditor();
    }

    private void SyncProjectGroups(List<NoteItemViewModel> notes)
    {
        var groups = notes
            .GroupBy(vm => _projects.DisplayName(vm.Project))
            .OrderBy(g => g.Key, ProjectCatalog.Comparer)
            .Select(g =>
            {
                var group = ProjectGroups.FirstOrDefault(pg => pg.Name == g.Key) ?? new ProjectGroupViewModel { Name = g.Key };
                group.ColorBrush = _projects.BrushOf(g.Key) ?? Brushes.Gray;
                SyncWindow(group.Notes, g.ToList());
                return group;
            })
            .ToList();

        foreach (var gone in ProjectGroups.Except(groups))
            SyncWindow(gone.Notes, []);
        ProjectGroups.SyncTo(groups);
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

        // Write only the notes whose position changed, and leave their "updated"
        // date alone: moving a note in a list is not editing it.
        for (int i = 0; i < group.Count; i++)
        {
            if (group[i].Note.SortOrder != i)
                group[i].SetSortOrder(i);
        }
        RefreshNotes();
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
        foreach (var vm in OnScreen.ToList())
            if (vm.IsExpanded) vm.CancelEditsCommand.Execute(null);
    }

    /// <summary>Ctrl+S, in every layout.</summary>
    public void SaveOpenEditors()
    {
        SaveSplitEditor();
        foreach (var vm in OnScreen.ToList())
            if (vm.IsExpanded) vm.SaveEditsCommand.Execute(null);
    }

    // Not a full refresh: that could drop the old project from the list while the
    // header dropdown still has it selected, and the dropdown would answer with null.
    partial void OnSelectedProjectChanged(string value) => UpdateWindows();
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
        CloseCards();
        var settings = _settingsService.Load();
        settings.GroupByProject = value;
        _settingsService.Save(settings);
        OnPropertyChanged(nameof(ShowProjectView));
        OnPropertyChanged(nameof(ShowPriorityView));
        RefreshNotes();
    }
    partial void OnSortByDateChanged(bool value)
    {
        CloseCards();
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
        CloseCards();

        var settings = _settingsService.Load();
        settings.ViewMode = newValue;
        settings.GridView = newValue == "grid"; // keep legacy flag in sync
        _settingsService.Save(settings);
        OnPropertyChanged(nameof(IsGridLayout));
        OnPropertyChanged(nameof(IsSplitLayout));
        OnPropertyChanged(nameof(SelectedViewMode));

        UpdateWindows();
    }

    // The split detail pane is always editable: save the note being left, and load
    // the one being entered.
    partial void OnSelectedSplitNoteChanged(NoteItemViewModel? oldValue, NoteItemViewModel? newValue)
    {
        oldValue?.CloseEditor();
        newValue?.LoadEditor();
        if (newValue != null)
            _splitSelectionPath = newValue.Note.FilePath;
    }

    /// <summary>Flush the split detail pane to disk. Safe to call anytime; it does
    /// nothing when nothing changed. Used by Ctrl+S, window blur, close and exit.</summary>
    public void SaveSplitEditor() => SelectedSplitNote?.SaveEditor();

    /// <summary>Title a brand-new note gets until the user types one (Standard
    /// Notes convention). It doubles as the file-name slug, so it must never be
    /// blank; the view selects it on focus so typing replaces it.</summary>
    public static string DefaultNewNoteTitle(DateTime created) =>
        created.ToString("dddd, MMM d, yyyy 'at' h:mm tt");

    /// <summary>
    /// The one create path for every layout (the old modal is gone). Saves a
    /// real file at once, then reveals the note where the user is looking:
    /// selected in the split pane (editing primed), or expanded inline in the
    /// list/grid. Anything that would hide a fresh Open note (search text, the
    /// Done filter) is cleared first. Returns the item so the view can focus it.
    /// </summary>
    public NoteItemViewModel CreateNote()
    {
        var note = new Note
        {
            Title = DefaultNewNoteTitle(DateTime.Now),
            Project = SelectedProject == ProjectCatalog.AllProjects ? "" : SelectedProject,
        };
        _noteService.Save(note);

        if (StatusFilter == "done") StatusFilter = "all";
        if (!string.IsNullOrEmpty(SearchText))
        {
            SearchText = "";
            _searchDebounce?.Stop(); // the refresh below covers it
        }
        RefreshNotes();

        var item = _items.First(v => v.Note.FilePath == note.FilePath);
        if (IsSplitLayout)
            SelectedSplitNote = item;
        else
            item.Expand();
        return item;
    }

    partial void OnIsSelectModeChanged(bool value)
    {
        foreach (var vm in _items)
        {
            vm.IsSelectMode = value;
            if (!value) vm.IsSelected = false;
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void ExportSelected()
    {
        var selected = OnScreen.Where(vm => vm.IsSelected).Select(vm => vm.Note).ToList();
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
