using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JLNotes.ViewModels;

public partial class ProjectGroupViewModel : ObservableObject
{
    public string Name { get; set; } = "";

    [ObservableProperty]
    private Brush _colorBrush = Brushes.Gray;

    public ObservableCollection<NoteItemViewModel> Notes { get; } = [];
}
