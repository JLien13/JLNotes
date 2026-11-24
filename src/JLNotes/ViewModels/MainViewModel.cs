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
    private readonly NoteIndex _links = new();
    private DispatcherTimer? _searchDebounce;
    private DispatcherTimer? _refreshDebounce;
    private DateTime? _refreshPendingSince;
    private bool _refreshing;

    // Folder changes arriving within this long make one refresh. A sync tool bringing
    // in many notes at once (another PC's changes) would otherwise reload every note
    // once per file.
    private static readonly TimeSpan RefreshAfterQuiet = TimeSpan.FromMilliseconds(300);
    // ...but a long burst still shows what has arrived at least this often.
    private static readonly TimeSpan RefreshAtLeastEvery = TimeSpan.FromSeconds(2);
    private DispatcherTimer? _noticeTimer;

    // What the notice bar's button does; null when the bar is hidden.
    private Action? _noticeAction;

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

    /// <summary>The notice bar at the bottom of the panel (Deleted "Plan" / Undo,
    /// or a save that wrote over an outside change); null hides the bar.</summary>
    [ObservableProperty]
    private string? _noticeText;

    /// <summary>The notice bar's button text.</summary>
    [ObservableProperty]
    private string _noticeActionText = "";

    /// <summary>How long the notice bar stays up.</summary>
    private static readonly TimeSpan NoticeShownFor = TimeSpan.FromSeconds(15);

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
    public ProjectCatalog ProjectCatalog => _projects;
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

        _links.OpenRequested += ShowNote;
        _noteService.ClashFound += note => ShowNotice(
            $"“{note.Title}” was also changed somewhere else. Both versions are kept.", "Choose",
            () =>
            {
                if (_items.FirstOrDefault(v => SamePath(v.Note.FilePath, note.FilePath)) is not { } item) return;
                Reveal(item);
                item.ChooseVersionCommand.Execute(null);
            });
        RefreshNotes();

        _refreshDebounce = new DispatcherTimer { Interval = RefreshAfterQuiet };
        _refreshDebounce.Tick += (_, _) => RefreshAfterChanges();
        _noteService.NotesChanged += () =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _refreshPendingSince ??= DateTime.UtcNow;
                _refreshDebounce.Stop(); // restart: refresh once things go quiet
                if (DateTime.UtcNow - _refreshPendingSince >= RefreshAtLeastEvery)
                    RefreshAfterChanges();
                else
                    _refreshDebounce.Start();
            });
        _noteService.StartWatching();

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            UpdateWindows();
        };
        _noticeTimer = new DispatcherTimer { Interval = NoticeShownFor };
        _noticeTimer.Tick += (_, _) => ShowNotice(null);
    }

    /// <summary>Reads the notes folder again and brings the one list up to date.
    /// A note that is still there keeps its view model, so its row, selection and
    /// open editor all survive.</summary>
    public void RefreshNotes()
    {
        var notes = _noteService.LoadAll();
        var settings = _settingsService.Load();
        _projects.Refresh(_projectService.Load(), settings.ProjectRepos, notes, SelectedProject);
        _links.Refresh(notes);
        var subtitleDisplay = settings.SubtitleDisplay;

        // One row per file. A second one on the same file (typing that could not be kept
        // as its own note last time) is handled with the gone ones below and tried again.
        var previous = new Dictionary<string, NoteItemViewModel>(StringComparer.OrdinalIgnoreCase);
        var extra = _items.Where(vm => !previous.TryAdd(vm.Note.FilePath, vm)).ToList();
        var matched = MatchItems(notes, previous);
        var selectedBefore = SelectedSplitNote;
        _items = notes.Select(note =>
        {
            if (!matched.TryGetValue(note, out var vm))
                return CreateItem(note);
            vm.Reload(note);
            return vm;
        }).ToList();
        foreach (var vm in _items)
            vm.SubtitleDisplay = subtitleDisplay;

        // A note whose file is there but could not be read just now (the sync tool has it
        // open) is not gone: one open in an editor stays as it was, typing and all, and the
        // next refresh reads it; others drop out of the list until then, as reported.
        // Only a note whose file is really gone has its unsaved typing kept as a new note
        // (saving its editor as it is would bring the deleted file back).
        // A note whose file now holds a different note (another PC's with the same name,
        // shown by its own row) is handled like a gone one: its typing becomes its own note.
        var rescued = new List<(Note Note, string WasTitled, bool Replaced, bool WasOpen)>();
        foreach (var gone in previous.Values.Concat(extra))
        {
            var replaced = _items.Any(vm => SamePath(vm.Note.FilePath, gone.Note.FilePath));
            if (System.IO.File.Exists(gone.Note.FilePath) && !replaced)
            {
                if (gone.HasOpenEditor)
                    _items.Add(gone);
                else
                    gone.CancelEditsCommand.Execute(null);
                continue;
            }
            try
            {
                var wasTitled = gone.Note.Title;
                if (gone.KeepUnsavedAsNewNote() is { } kept)
                    rescued.Add((kept, wasTitled, replaced, gone == selectedBefore));
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                _items.Add(gone); // could not be written now: keep the typing on screen
            }
        }

        // The open note stays open even when its file took a new name.
        if (SelectedSplitNote is { } selected && _items.Contains(selected))
            _splitSelectionPath = selected.Note.FilePath;

        OnPropertyChanged(nameof(UnreadableNotesMessage));
        OnPropertyChanged(nameof(UnreadableNotesDetail));
        _refreshing = true;
        try { UpdateWindows(); }
        finally { _refreshing = false; }

        if (rescued.Count > 0)
        {
            RefreshNotes();
            var (first, wasTitled, replaced, wasOpen) = rescued[0];
            if (wasOpen)
                ShowNote(first);
            var more = rescued.Count > 1 ? $" {rescued.Count - 1} more were kept the same way." : "";
            ShowNotice((replaced
                    ? $"“{wasTitled}” was replaced somewhere else by a different note with the same name while you were typing in it. What you typed is kept as a note of its own."
                    : $"“{wasTitled}” was deleted somewhere else while you were typing in it. What you typed is kept as a new note.") + more,
                "Show", () => ShowNote(first));
        }
    }

    private void RefreshAfterChanges()
    {
        _refreshDebounce?.Stop();
        _refreshPendingSince = null;
        RefreshNotes();
    }

    /// <summary>
    /// Which view model each note keeps, so its row, selection and open editor carry on:
    /// the one showing its file, unless that file now holds a different note
    /// (<see cref="Note.AreSameNote"/>); otherwise, for a note whose file took a new name
    /// (a sync tool gave it a conflicted-copy name because another PC made a note with the
    /// same name), the one that surely showed it under its old name. Matched view models
    /// are taken out of <paramref name="previous"/>, which is left holding the ones whose
    /// note is gone.
    /// </summary>
    private static Dictionary<Note, NoteItemViewModel> MatchItems(List<Note> notes, Dictionary<string, NoteItemViewModel> previous)
    {
        var kept = new Dictionary<Note, NoteItemViewModel>();
        foreach (var note in notes)
        {
            if (previous.TryGetValue(note.FilePath, out var vm) && Note.AreSameNote(vm.Note, note) != false)
            {
                kept[note] = vm;
                previous.Remove(note.FilePath);
            }
        }
        foreach (var (path, vm) in previous.ToList())
        {
            if (notes.Where(n => !kept.ContainsKey(n) && Note.AreSameNote(n, vm.Note) == true).ToList() is [var moved])
            {
                kept[moved] = vm;
                previous.Remove(path);
            }
        }
        return kept;
    }

    private NoteItemViewModel CreateItem(Note note)
    {
        var vm = new NoteItemViewModel(note, _noteService, _settingsService, _projects, _links) { IsSelectMode = IsSelectMode };
        vm.NoteChanged += RefreshNotes;
        vm.NoteDeleted += kept =>
        {
            if (kept != null)
                ShowNotice($"Deleted “{kept.Read().Title}”", "Undo", () => RestoreDeleted(kept));
            RefreshNotes();
        };
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
            PassesStatusFilter(vm) &&
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

    private bool PassesStatusFilter(NoteItemViewModel vm) => StatusFilter switch
    {
        "open" => vm.Note.Status != NoteStatus.Done,
        "done" => vm.Note.Status == NoteStatus.Done,
        _ => true
    };

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
        // Opening a note asks about a second version; a refresh moving the selection
        // (or the first note being picked at start) does not.
        if (!_refreshing)
            newValue?.AskIfClash();
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
        RefreshNotes();

        var item = _items.First(v => SamePath(v.Note.FilePath, note.FilePath));
        Reveal(item);
        return item;
    }

    /// <summary>Brings a note up where the user is looking, as a link click does.</summary>
    public void ShowNote(Note note)
    {
        if (_items.FirstOrDefault(v => SamePath(v.Note.FilePath, note.FilePath)) is { } item)
            Reveal(item);
    }

    /// <summary>
    /// The one way a note is brought into view (a new note, a link, an undo): any
    /// filter that hides it is cleared, then it is selected in the split pane or
    /// opened inline in the list/grid.
    /// </summary>
    private void Reveal(NoteItemViewModel item)
    {
        if (!ProjectCatalog.PassesFilter(item.Project, SelectedProject))
            SelectedProject = ProjectCatalog.AllProjects;
        if (!PassesStatusFilter(item))
            StatusFilter = "all";
        if (!Helpers.NoteSearch.Matches(item.Note, Helpers.NoteSearch.Parse(SearchText)))
        {
            SearchText = "";
            _searchDebounce?.Stop(); // the update below covers it
        }
        UpdateWindows();

        if (IsSplitLayout)
            SelectedSplitNote = item;
        else
            item.Expand();
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Shows the notice bar for a while, or hides it (null text).</summary>
    private void ShowNotice(string? text, string actionText = "", Action? action = null)
    {
        _noticeTimer?.Stop();
        NoticeText = text;
        NoticeActionText = actionText;
        _noticeAction = action;
        if (text != null)
            _noticeTimer?.Start();
    }

    [RelayCommand]
    private void NoticeAction()
    {
        var action = _noticeAction;
        ShowNotice(null);
        action?.Invoke();
    }

    [RelayCommand]
    private void DismissNotice() => ShowNotice(null);

    /// <summary>Brings a deleted note back (the undo bar, or the Deleted notes window) and shows it.</summary>
    public void RestoreDeleted(NoteVersion kept)
    {
        _noteService.Restore(kept);
        RefreshNotes();
        if (_items.FirstOrDefault(v => SamePath(v.Note.FilePath, kept.NotePath)) is { } item)
            Reveal(item);
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
