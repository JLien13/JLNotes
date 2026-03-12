using System.Windows;
using System.Windows.Input;

namespace JLNotes.Views;

public partial class NoteDetailWindow : Window
{
    public NoteDetailWindow()
    {
        InitializeComponent();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}
