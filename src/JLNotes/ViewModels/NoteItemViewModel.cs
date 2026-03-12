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
    private readonly Note _note;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _editTitle;
    [ObservableProperty] private FlowDocument? _editDocument;
    [ObservableProperty] private string _editProject;
    [ObservableProperty] private string _editBranch;
    [ObservableProperty] private string _editRepo;
    [ObservableProperty] private string _editTags;

    public NoteItemViewModel(Note note, NoteService noteService, SettingsService settingsService, ProjectService projectService)
    {
        _note = note;
        _noteService = noteService;
        _settingsService = settingsService;
        _projectService = projectService;
        _editTitle = note.Title;
        _editProject = note.Project;
        _editBranch = note.Branch;
        _editRepo = note.Repo;
        _editTags = string.Join(", ", note.Tags);
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

    public string SubtitleDisplay => _settingsService.Load().SubtitleDisplay;

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
            OnPropertyChanged();
            NoteChanged?.Invoke();
        }
    }

    public event Action? NoteChanged;
    public event Action? NoteDeleted;
    public event Action<NoteItemViewModel, NoteItemViewModel>? DropReceived;

    public Brush PriorityBrush => Priority switch
    {
        NotePriority.High => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        NotePriority.Medium => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        NotePriority.Low => new SolidColorBrush(Color.FromRgb(0x64, 0x9E, 0xCF)),
        _ => Brushes.Gray
    };

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded)
        {
            EditTitle = _note.Title;
            var attachDir = _noteService.GetAttachmentsDir(_note);
            EditDocument = FlowDocumentHelper.BuildDocument(_note.Body, attachDir);
            EditProject = _note.Project;
            EditBranch = _note.Branch;
            EditRepo = _note.Repo;
            EditTags = string.Join(", ", _note.Tags);
        }
    }

    [RelayCommand]
    private void SaveEdits()
    {
        _note.Title = EditTitle;
        if (EditDocument != null)
        {
            _note.Body = FlowDocumentHelper.SerializeDocument(EditDocument);
            _note.Attachments = FlowDocumentHelper.GetAttachmentFilenames(EditDocument);
        }
        _note.Project = EditProject;
        _note.Branch = EditBranch;
        _note.Repo = EditRepo;
        _note.Tags = EditTags
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        _noteService.Save(_note);
        IsExpanded = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(Branch));
        OnPropertyChanged(nameof(Repo));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(HasTags));
        NoteChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelEdits()
    {
        IsExpanded = false;
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
        _noteService.Save(_note);
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(PriorityBrush));
        NoteChanged?.Invoke();
    }

    [RelayCommand]
    private void ToggleStatus()
    {
        IsDone = !IsDone;
        _noteService.Save(_note);
        NoteChanged?.Invoke();
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
            var project = _projectService.Load()
                .FirstOrDefault(p => p.Name.Equals(Project, StringComparison.OrdinalIgnoreCase));
            repoPath = project?.Repo ?? "";
        }

        if (string.IsNullOrEmpty(repoPath) || !System.IO.Directory.Exists(repoPath))
        {
            System.Windows.MessageBox.Show(
                $"No valid repo path found for project \"{Project}\".\nSet a repo path in projects.json or on the note.",
                "Open in Claude", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Build context prompt from the note
        var prompt = $"I'm working on: {Title}";
        if (!string.IsNullOrEmpty(Branch))
            prompt += $" (branch: {Branch})";
        prompt += $"\n\n{Body}";
        if (!string.IsNullOrEmpty(_note.FilePath))
            prompt += $"\n\nNote file: {_note.FilePath}";

        // Escape quotes for cmd
        var escapedPrompt = prompt.Replace("\"", "\\\"");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start cmd /k \"cd /d \"{repoPath}\" && claude \"{escapedPrompt}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        System.Diagnostics.Process.Start(startInfo);
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
            var attachDir = _noteService.GetAttachmentsDir(_note);
            FlowDocumentHelper.InsertAttachment(richTextBox, fileName, attachDir);
        }
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
