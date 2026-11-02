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

    public NoteItemViewModel(Note note, NoteService noteService, SettingsService settingsService, ProjectCatalog projects)
    {
        _note = note;
        _noteService = noteService;
        _settingsService = settingsService;
        _projects = projects;
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
    public event Action? NoteDeleted;
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
        var editorFollows = EditDocument != null && !EditorHasChanges();
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
        EditDocument = FlowDocumentHelper.BuildDocument(_note.Body, AttachmentsDir);
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
        _noteService.Save(_note, stampUpdated);
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
        _noteService.Delete(_note);
        NoteDeleted?.Invoke();
    }

    [RelayCommand]
    private void OpenInClaude()
    {
        // Resolve repo path: try note's Repo field as absolute path first,
        // then fall back to project's registered repo path
        var repoPath = Repo;
        if (string.IsNullOrEmpty(repoPath) || !System.IO.Directory.Exists(repoPath))
        {
            repoPath = _projects.RepoOf(Project);
        }

        if (string.IsNullOrEmpty(repoPath) || !System.IO.Directory.Exists(repoPath))
        {
            System.Windows.MessageBox.Show(
                $"No valid repo path found for project \"{Project}\".\nSet a repo path in projects.json or on the note.",
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
