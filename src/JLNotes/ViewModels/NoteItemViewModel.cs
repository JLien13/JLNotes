using System.Collections.ObjectModel;
using System.Windows.Documents;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLNotes.Helpers;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.ViewModels;

public partial class NoteItemViewModel : ObservableObject
{
    private Note _note;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectCatalog _projects;
    private readonly NoteIndex _links;

    // The file text what is on screen was built from. A save tells NoteService, so a
    // change made meanwhile on another PC (or by Claude) is held as a second version
    // instead of being written over. It follows the file whenever nothing is unsaved.
    private string? _baseText;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private FlowDocument? _editDocument;
    [ObservableProperty] private string _editProject = "";
    [ObservableProperty] private string _editBranch = "";
    [ObservableProperty] private string _editRepo = "";
    [ObservableProperty] private string _editTags = "";

    // Split pane: true while the comma-separated tag editor replaces the chips.
    [ObservableProperty] private bool _isEditingTags;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSelectMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubtitleText))]
    [NotifyPropertyChangedFor(nameof(ShowTags))]
    [NotifyPropertyChangedFor(nameof(ShowSubtitleText))]
    private string _subtitleDisplay = "project";

    /// <param name="links">The main view's index of notes, which draws [[note]] links
    /// and answers "linked from". Without one (some tests) a note links nowhere.</param>
    public NoteItemViewModel(Note note, NoteService noteService, SettingsService settingsService, ProjectCatalog projects,
        NoteIndex? links = null)
    {
        _note = note;
        _noteService = noteService;
        _settingsService = settingsService;
        _projects = projects;
        _links = links ?? new NoteIndex();
        _baseText = noteService.LastSeenText(note.FilePath);
    }

    public Note Note => _note;
    public string Title => _note.Title;
    public string Project => _note.Project;
    public NotePriority Priority => _note.Priority;
    public string Body => _note.Body;
    public string Branch => _note.Branch;
    public string Repo => _note.Repo;
    public DateTime Created => _note.Created;
    public List<string> Tags => _note.Tags;
    public string AttachmentsDir => _noteService.GetAttachmentsDir(_note);

    public string TimeAgo
    {
        get
        {
            var span = DateTime.Now - Created;
            if (span.TotalSeconds < 0) return Created.ToString("MMM d");
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return Created.ToString("MMM d");
        }
    }

    public bool HasTags => Tags.Count > 0;

    /// <summary>The note is open in an editor (the split pane, or an opened card).</summary>
    public bool HasOpenEditor => EditDocument != null;

    /// <summary>The note was changed in two places and asks which version to keep.</summary>
    public bool HasClash => _noteService.HasClash(_note.FilePath);

    /// <summary>Other notes with a [[link]] to this one.</summary>
    public IReadOnlyList<Note> LinkedFrom => _links.LinkedFrom(_note);
    public bool HasLinkedFrom => LinkedFrom.Count > 0;

    public string SubtitleText => SubtitleDisplay switch
    {
        "repo" => Repo,
        "project" => Project,
        _ => ""
    };

    public bool ShowTags => SubtitleDisplay == "tags" && Tags.Count > 0;
    public bool ShowSubtitleText => SubtitleDisplay != "tags" && !string.IsNullOrEmpty(SubtitleText);

    public bool IsDone
    {
        get => _note.Status == NoteStatus.Done;
        set
        {
            _note.Status = value ? NoteStatus.Done : NoteStatus.Open;
            Save();
            NoteChanged?.Invoke();
        }
    }

    public string StatusMenuText => IsDone ? "Mark Open" : "Mark Done";

    public event Action? NoteChanged;
    /// <summary>Carries the version history kept, which is what undo puts back.</summary>
    public event Action<NoteVersion?>? NoteDeleted;
    public event Action<NoteItemViewModel, NoteItemViewModel>? DropReceived;

    public Brush ProjectColorBrush => _projects.BrushOf(Project) ?? Brushes.Transparent;

    /// <summary>Split pane project picker items: the same projects as the header.</summary>
    public ObservableCollection<string> ProjectChoices => _projects.PickerChoices;

    /// <summary>Split pane project picker value. Setting it saves at once.</summary>
    public string SplitProjectChoice
    {
        get => _projects.DisplayName(EditProject);
        set
        {
            // A ComboBox reports null when its selection drops out of the list. That
            // is not the user choosing "no project", so it must not clear the project.
            if (value == null) return;
            var project = value == ProjectCatalog.NoProject ? "" : value;
            if (ProjectCatalog.Same(project, EditProject)) return;
            EditProject = project;
            OnPropertyChanged();
            SaveEditor();
        }
    }

    /// <summary>
    /// The file was read from disk again. This view model and any open editor carry
    /// on. An editor with nothing unsaved follows a change made outside the app; one
    /// with unsaved edits keeps them.
    /// </summary>
    public void Reload(Note fresh)
    {
        var fileChanged = fresh.ToMarkdown() != _note.ToMarkdown();
        var unsaved = EditDocument != null && EditorHasChanges();
        var editorFollows = EditDocument != null && !unsaved;
        if (!unsaved)
            _baseText = _noteService.LastSeenText(fresh.FilePath);
        _note = fresh;
        if (fileChanged && editorFollows)
            LoadEditor();
        RaiseDisplayChanged();
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (IsExpanded) CancelEdits();
        else Expand();
    }

    /// <summary>Open the inline edit form (list/grid layouts). Also how a freshly
    /// created note is revealed.</summary>
    public void Expand()
    {
        LoadEditor();
        IsExpanded = true;
        AskIfClash();
    }

    /// <summary>The Save button and Ctrl+S on a list card.</summary>
    [RelayCommand]
    private void SaveEdits()
    {
        CloseEditor();
        NoteChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelEdits()
    {
        IsExpanded = false;
        EditDocument = null;
        // Nothing unsaved now: a change made from here on starts from the file as it is.
        _baseText = _noteService.LastSeenText(_note.FilePath);
    }

    /// <summary>Save what the editor holds and let go of it. A new editor always
    /// builds a new document, because a FlowDocument cannot move to a second RichTextBox.</summary>
    public void CloseEditor()
    {
        SaveEditor();
        CancelEdits();
    }

    // The body as first drawn in the editor. Drawing and reading back can differ from
    // the stored text, so comparing against this is what keeps browsing from saving.
    private string? _editorBaseline;

    /// <summary>The one way note values reach the editor, for list cards and the split pane alike.</summary>
    public void LoadEditor()
    {
        EditTitle = _note.Title;
        EditProject = _note.Project;
        EditBranch = _note.Branch;
        EditRepo = _note.Repo;
        EditTags = FormatTags(_note.Tags);
        IsEditingTags = false;
        OnPropertyChanged(nameof(SplitProjectChoice));
        EditDocument = FlowDocumentHelper.BuildDocument(_note.Body, AttachmentsDir, _links);
        _editorBaseline = FlowDocumentHelper.SerializeDocument(EditDocument);
    }

    /// <summary>Saves the editor if it holds anything new; browsing never rewrites a file.</summary>
    public void SaveEditor()
    {
        if (EditorHasChanges())
            Save();
    }

    private bool EditorHasChanges() =>
        EditDocument != null &&
        (FlowDocumentHelper.SerializeDocument(EditDocument) != _editorBaseline
         || (EditTitle ?? "") != _note.Title
         || (EditProject ?? "") != _note.Project
         || (EditBranch ?? "") != _note.Branch
         || (EditRepo ?? "") != _note.Repo
         || !ParseTags(EditTags).SequenceEqual(_note.Tags));

    /// <summary>
    /// The one way a note reaches the disk. Whatever is open in the editor goes with
    /// it, so no save (a priority change, a tick, a drag) ever writes a body older
    /// than the one on screen.
    /// </summary>
    private void Save(bool stampUpdated = true)
    {
        if (EditorHasChanges())
        {
            var body = FlowDocumentHelper.SerializeDocument(EditDocument!);
            if (body != _editorBaseline)
            {
                _note.Body = body.Trim(); // reading a file trims too
                _editorBaseline = body;
            }
            _note.Title = EditTitle ?? "";
            _note.Project = EditProject ?? "";
            _note.Branch = EditBranch ?? "";
            _note.Repo = EditRepo ?? "";
            _note.Tags = ParseTags(EditTags);
            stampUpdated = true;
        }
        _noteService.Save(_note, stampUpdated, _baseText);
        _baseText = _noteService.LastSeenText(_note.FilePath);
        RaiseDisplayChanged();
    }

    private void RaiseDisplayChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(Branch));
        OnPropertyChanged(nameof(Repo));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(ShowTags));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(ShowSubtitleText));
        OnPropertyChanged(nameof(ProjectColorBrush));
        OnPropertyChanged(nameof(TimeAgo));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(StatusMenuText));
        OnPropertyChanged(nameof(LinkedFrom));
        OnPropertyChanged(nameof(HasLinkedFrom));
        OnPropertyChanged(nameof(HasClash));
    }

    /// <summary>The one tag parser: comma-separated text to a trimmed,
    /// de-duplicated (case-sensitive) list.</summary>
    public static List<string> ParseTags(string? text) =>
        (text ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();

    /// <summary>The reverse of <see cref="ParseTags"/>.</summary>
    public static string FormatTags(IEnumerable<string> tags) => string.Join(", ", tags);

    /// <summary>A drag gave this note a new position. Moving a note is not editing
    /// it, so its "updated" date stays unless the editor also held changes.</summary>
    public void SetSortOrder(int sortOrder)
    {
        _note.SortOrder = sortOrder;
        Save(stampUpdated: false);
    }

    [RelayCommand]
    private void SetPriorityHigh() => SetPriority(NotePriority.High);

    [RelayCommand]
    private void SetPriorityMedium() => SetPriority(NotePriority.Medium);

    [RelayCommand]
    private void SetPriorityLow() => SetPriority(NotePriority.Low);

    private void SetPriority(NotePriority priority)
    {
        _note.Priority = priority;
        Save();
        NoteChanged?.Invoke();
    }

    [RelayCommand]
    private void ToggleStatus()
    {
        IsDone = !IsDone;
    }

    [RelayCommand]
    private void Delete()
    {
        var settings = _settingsService.Load();
        if (settings.ConfirmDelete)
        {
            var result = System.Windows.MessageBox.Show(
                $"Delete \"{_note.Title}\"?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes)
                return;
        }
        CancelEdits(); // nothing may save this note back after it is gone
        var kept = _noteService.Delete(_note);
        NoteDeleted?.Invoke(kept);
    }

    /// <summary>A name in the "Linked from" list was clicked.</summary>
    [RelayCommand]
    private void OpenNote(Note note) => _links.RequestOpen(note.FilePath);

    /// <summary>Puts an earlier version back. What is on screen is saved first, so
    /// it is kept in history too and the restore can itself be undone.</summary>
    public void RestoreVersion(NoteVersion version)
    {
        SaveEditor();
        _noteService.Restore(version);
        NoteChanged?.Invoke();
    }

    [RelayCommand]
    private void ShowHistory()
    {
        SaveEditor();
        var window = new Views.HistoryWindow($"Earlier versions of “{Title}”",
            _noteService.GetVersions(_note.FilePath), RestoreVersion)
        {
            Owner = System.Windows.Application.Current?.MainWindow is { IsVisible: true } main ? main : null
        };
        window.ShowDialog();
    }

    /// <summary>Settles a waiting version as chosen: the one that was shown, or else the
    /// first. What is on screen is saved first, so "keep this" keeps what the user sees.</summary>
    public void Resolve(ClashChoice choice, NoteVersion? shown = null)
    {
        try
        {
            SaveEditor();
            if ((shown ?? _noteService.GetClashes(_note.FilePath).FirstOrDefault()) is { } clash)
                _noteService.Resolve(clash, choice);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            // The sync tool held a file, so the choice may be only partly made. What was
            // written shows, nothing is lost, and the note still asks if it needs to.
        }
        RaiseDisplayChanged();
        NoteChanged?.Invoke();
    }

    /// <summary>This note's file is gone (deleted here or on another PC), or now holds a
    /// different note (another PC's, with the same name). Typing not yet saved is kept as
    /// a note of its own rather than lost, with its pictures, and that note is returned;
    /// null when nothing was unsaved. Either way the editor is let go.</summary>
    public Note? KeepUnsavedAsNewNote()
    {
        Note? kept = null;
        if (EditDocument != null && EditorHasChanges())
        {
            Save(); // the file is gone or holds another note: this makes a note under a free name
            kept = _note;
        }
        CancelEdits();
        return kept;
    }

    private bool _asking;

    /// <summary>Opening a note that was changed in two places asks which version to keep.
    /// The question waits until the note is on screen; "Decide later" leaves it until the
    /// note is opened again (the bar on the note can also ask).</summary>
    public void AskIfClash()
    {
        if (!HasClash || _asking) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                // Only while the user is looking: never at login, or with the panel hidden.
                if (HasClash && !_asking && System.Windows.Application.Current?.MainWindow is { IsVisible: true, IsActive: true })
                    ChooseVersionCommand.Execute(null);
            });
    }

    /// <summary>Shows this note and its other version side by side to pick from.</summary>
    [RelayCommand]
    private void ChooseVersion()
    {
        SaveEditor();
        if (_noteService.GetClashes(_note.FilePath).FirstOrDefault() is not { } clash) return;
        Note current, other;
        try
        {
            current = System.IO.File.Exists(_note.FilePath)
                ? Note.ParseFromMarkdown(System.IO.File.ReadAllText(_note.FilePath), _note.FilePath)
                : _note;
            other = clash.Read();
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return; // settled on another PC just now, or mid-sync: the bar stays if still needed
        }
        var chose = false;
        _asking = true;
        try
        {
            new Views.ClashWindow(current, other, choice => { chose = true; Resolve(choice, clash); })
            {
                Owner = System.Windows.Application.Current?.MainWindow is { IsVisible: true } main ? main : null
            }.ShowDialog();
        }
        finally
        {
            _asking = false;
        }
        if (chose && HasClash)
            AskIfClash(); // another version still waiting (three PCs, or more)
    }

    [RelayCommand]
    private void CopyLink()
    {
        try { System.Windows.Clipboard.SetText(_note.GetLinkText()); } catch { /* clipboard contention */ }
    }

    [RelayCommand]
    private void OpenInClaude()
    {
        // Claude opens in the project's repo folder on this PC. The note's own repo field
        // is a name to read, not a folder.
        var repoPath = _projects.RepoOf(Project);
        if (!System.IO.Directory.Exists(repoPath))
        {
            System.Windows.MessageBox.Show(
                string.IsNullOrWhiteSpace(Project)
                    ? "This note has no project. Give it one, then pick the project's repo folder in Settings > Projects."
                    : $"Project \"{Project}\" has no repo folder on this PC, or the folder is gone.\nPick it in Settings > Projects.",
                "Open in Claude", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Build a short prompt that points Claude at the note file on disk.
        // Launched via PowerShell -EncodedCommand (Base64 UTF-16) so nothing
        // user-controlled (title, path, branch) ever touches the shell raw —
        // em-dashes, parens, &, |, %, !, quotes, Unicode all pass through intact.
        var prompt = $"Read my note at '{_note.FilePath}' and help me work on: {Title}";
        if (!string.IsNullOrEmpty(Branch))
            prompt += $" (branch: {Branch})";

        // Escape ALL single quotes (literals AND any in the user's title/path)
        // for PowerShell's single-quoted string syntax: ' -> ''
        var psEscaped = prompt.Replace("'", "''");
        var psCommand = $"& claude '{psEscaped}'";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psCommand));

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -EncodedCommand {encoded}",
            WorkingDirectory = repoPath,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(startInfo);
    }

    [RelayCommand]
    private void CopyPath()
    {
        try { System.Windows.Clipboard.SetText(_note.FilePath); } catch { /* clipboard contention */ }
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        if (!System.IO.File.Exists(_note.FilePath)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_note.FilePath}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void ExportToWord()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Note to Word",
            Filter = "Word Document|*.docx",
            FileName = $"{_note.GetSlug()}.docx"
        };
        if (dialog.ShowDialog() == true)
        {
            ExportService.ExportToWord([_note], dialog.FileName, _noteService.GetAttachmentsDir);
        }
    }

    public void HandleImageDrop(string[] filePaths, System.Windows.Controls.RichTextBox richTextBox)
    {
        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

        foreach (var path in filePaths)
        {
            var ext = System.IO.Path.GetExtension(path);
            if (!imageExtensions.Contains(ext)) continue;

            var fileName = _noteService.AddAttachment(_note, path);
            FlowDocumentHelper.InsertAttachment(richTextBox, fileName, AttachmentsDir);
        }
    }

    public void HandleImagePaste(System.Windows.Media.Imaging.BitmapSource image, System.Windows.Controls.RichTextBox richTextBox)
    {
        var fileName = _noteService.AddAttachmentFromImage(_note, image);
        FlowDocumentHelper.InsertAttachment(richTextBox, fileName, AttachmentsDir);
    }

    public void HandleImageUpload(System.Windows.Controls.RichTextBox richTextBox)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
            Multiselect = true,
            Title = "Add Images"
        };
        if (dialog.ShowDialog() == true)
        {
            HandleImageDrop(dialog.FileNames, richTextBox);
        }
    }

    public void AcceptDrop(NoteItemViewModel source)
    {
        DropReceived?.Invoke(source, this);
    }
}
