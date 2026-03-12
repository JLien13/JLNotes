using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.ViewModels;

public partial class NoteDetailViewModel : ObservableObject
{
    private readonly NoteService _noteService;
    private readonly Note _note;
    private readonly bool _isNew;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _project;
    [ObservableProperty] private string _priorityText;
    [ObservableProperty] private string _body;
    [ObservableProperty] private string _branch;
    [ObservableProperty] private string _repo;

    public event Action? Saved;
    public event Action? Closed;

    public NoteDetailViewModel(Note note, NoteService noteService, bool isNew = false)
    {
        _note = note;
        _noteService = noteService;
        _isNew = isNew;
        _title = note.Title;
        _project = note.Project;
        _priorityText = note.Priority.ToString();
        _body = note.Body;
        _branch = note.Branch;
        _repo = note.Repo;
    }

    [RelayCommand]
    private void Save()
    {
        _note.Title = Title;
        _note.Project = Project;
        _note.Priority = Enum.TryParse<NotePriority>(PriorityText, true, out var p) ? p : NotePriority.Medium;
        _note.Body = Body;
        _note.Branch = Branch;
        _note.Repo = Repo;
        _noteService.Save(_note);
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Closed?.Invoke();
    }
}
