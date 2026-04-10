using System.Collections.ObjectModel;
using System.Windows.Media;

namespace JLNotes.ViewModels;

public class ProjectGroupViewModel
{
    public string Name { get; set; } = "";
    public Brush ColorBrush { get; set; } = Brushes.Gray;
    public ObservableCollection<NoteItemViewModel> Notes { get; } = [];
}
