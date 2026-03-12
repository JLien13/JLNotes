using System.Windows;
using System.Windows.Input;

namespace JLNotes.Views;

public partial class NoteWidgetWindow : Window
{
    public NoteWidgetWindow()
    {
        InitializeComponent();
    }

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseWidget_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
