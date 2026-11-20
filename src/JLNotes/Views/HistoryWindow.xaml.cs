using System.Windows;
using System.Windows.Input;
using JLNotes.Models;

namespace JLNotes.Views;

public partial class HistoryWindow : Window
{
    private readonly Action<NoteVersion> _restore;

    /// <param name="versions">Newest first, as NoteService gives them.</param>
    /// <param name="restore">Puts the chosen version back (the caller refreshes and shows it).</param>
    public HistoryWindow(string heading, IReadOnlyList<NoteVersion> versions, Action<NoteVersion> restore)
    {
        InitializeComponent();
        _restore = restore;
        HeadingText.Text = heading;

        var entries = versions.Select(v => new Entry(v, v.Read())).ToList();
        VersionList.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Preview.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (entries.Count > 0)
            VersionList.SelectedIndex = 0;
    }

    /// <summary>One line in the list: when this text was replaced, and its title then.</summary>
    private sealed record Entry(NoteVersion Version, Note Note)
    {
        public string When => Version.SavedAt.ToString("MMM d, yyyy  h:mm:ss tt");
        public string Title => Note.Title;
    }

    private void VersionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var entry = VersionList.SelectedItem as Entry;
        Preview.Text = entry == null ? "" : entry.Note.Title + Environment.NewLine + Environment.NewLine + entry.Note.Body;
        RestoreButton.IsEnabled = entry != null;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not Entry entry) return;
        _restore(entry.Version);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Heading_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
