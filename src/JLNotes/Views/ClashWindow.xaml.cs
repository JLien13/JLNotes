using System.Windows;
using System.Windows.Input;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Views;

public partial class ClashWindow : Window
{
    private readonly Action<ClashChoice> _choose;

    /// <param name="current">The note as it is now.</param>
    /// <param name="other">The other version, waiting for a decision.</param>
    /// <param name="choose">Settles it (the caller saves, refreshes and shows the result).
    /// Not called for "Decide later".</param>
    public ClashWindow(Note current, Note other, Action<ClashChoice> choose)
    {
        InitializeComponent();
        _choose = choose;
        HeadingText.Text = $"“{current.Title}” has two versions";
        ThisLabel.Text = $"In the note now (saved {When(current.Updated)})";
        OtherLabel.Text = $"The other version (saved {When(other.Updated)})";
        ThisText.Text = Shown(current);
        OtherText.Text = Shown(other);
    }

    private static string When(DateTime at) => at.ToString("MMM d, h:mm tt");

    private static string Shown(Note note)
    {
        var details = new List<string>();
        if (note.Project.Length > 0) details.Add($"Project: {note.Project}");
        details.Add($"Priority: {note.Priority.ToString().ToLowerInvariant()}");
        details.Add($"Status: {note.Status.ToString().ToLowerInvariant()}");
        if (note.Tags.Count > 0) details.Add($"Tags: {string.Join(", ", note.Tags)}");
        return note.Title + Environment.NewLine + string.Join(Environment.NewLine, details)
               + Environment.NewLine + Environment.NewLine + note.Body;
    }

    private void Choose(ClashChoice choice)
    {
        Close();
        _choose(choice);
    }

    private void KeepThis_Click(object sender, RoutedEventArgs e) => Choose(ClashChoice.KeepThis);

    private void KeepOther_Click(object sender, RoutedEventArgs e) => Choose(ClashChoice.KeepOther);

    private void Combine_Click(object sender, RoutedEventArgs e) => Choose(ClashChoice.Combine);

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Heading_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
