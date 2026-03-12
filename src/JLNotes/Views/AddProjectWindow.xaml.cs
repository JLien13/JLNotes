using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace JLNotes.Views;

public partial class AddProjectWindow : Window
{
    public string ProjectName { get; private set; } = "";
    public string RepoPath { get; private set; } = "";

    public AddProjectWindow()
    {
        InitializeComponent();
        NameBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select repository folder"
        };

        if (!string.IsNullOrEmpty(RepoBox.Text) && System.IO.Directory.Exists(RepoBox.Text))
            dialog.InitialDirectory = RepoBox.Text;

        if (dialog.ShowDialog(this) == true)
            RepoBox.Text = dialog.FolderName;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Enter a project name.", "Add Project",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectName = NameBox.Text.Trim();
        RepoPath = RepoBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
