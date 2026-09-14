using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;
    private List<ProjectInfo> _projects;
    private bool _initialized;

    public event Action? SettingsChanged;

    public SettingsWindow(SettingsService settingsService, ProjectService projectService)
    {
        _settingsService = settingsService;
        _projectService = projectService;
        _projects = _projectService.Load();
        InitializeComponent();

        var settings = _settingsService.Load();

        ThemeCombo.SelectedIndex = settings.Theme == "light" ? 1 : 0;
        SubtitleCombo.SelectedIndex = settings.SubtitleDisplay switch
        {
            "repo" => 1,
            "tags" => 2,
            _ => 0 // project
        };
        CloseBehaviorCombo.SelectedIndex = settings.CloseBehavior == "quit" ? 1 : 0;
        ConfirmDeleteCheck.IsChecked = settings.ConfirmDelete;
        AutoStartCheck.IsChecked = _settingsService.GetAutoStart();

        ProjectList.ItemsSource = _projects;

        VersionText.Text = $"Version {UpdateService.CurrentVersion}";

        _initialized = true;
    }

    // Settings face of the self-updater. First click checks; if a newer release
    // exists the button becomes "Install X" and the next click downloads + runs it.
    private Version? _pendingUpdate;

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;
        UpdateButton.IsEnabled = false;
        try
        {
            if (_pendingUpdate == null)
            {
                UpdateStatusText.Text = "Checking…";
                var res = await app.CheckForUpdateAsync(); // shared with the header link
                if (!res.Ok)
                    UpdateStatusText.Text = "Could not check for updates: " + res.Error;
                else if (res.UpToDate)
                    UpdateStatusText.Text = "You're on the latest version.";
                else
                {
                    _pendingUpdate = res.Latest;
                    UpdateStatusText.Text = $"Version {res.Latest} is available. Installing closes and reopens the app; your notes are untouched.";
                    UpdateButton.Content = $"Install {res.Latest}";
                }
                return;
            }

            var result = await app.RunUpdateAsync(text => UpdateStatusText.Text = text);
            if (!result.Launched)
            {
                _pendingUpdate = null;
                UpdateButton.Content = "Check for updates";
            }
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;

        var settings = _settingsService.Load();
        settings.Theme = ThemeCombo.SelectedIndex == 1 ? "light" : "dark";
        _settingsService.Save(settings);

        if (Application.Current is App app)
            app.ApplyTheme(settings.Theme);
    }

    private void SubtitleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;

        var settings = _settingsService.Load();
        settings.SubtitleDisplay = SubtitleCombo.SelectedIndex switch
        {
            1 => "repo",
            2 => "tags",
            _ => "project"
        };
        _settingsService.Save(settings);
        SettingsChanged?.Invoke();
    }

    private void CloseBehaviorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;

        var settings = _settingsService.Load();
        settings.CloseBehavior = CloseBehaviorCombo.SelectedIndex == 1 ? "quit" : "tray";
        _settingsService.Save(settings);
    }

    private void ConfirmDelete_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        var settings = _settingsService.Load();
        settings.ConfirmDelete = ConfirmDeleteCheck.IsChecked == true;
        _settingsService.Save(settings);
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        // Registry is the source of truth for auto-start; nothing to save in json.
        _settingsService.SetAutoStart(AutoStartCheck.IsChecked == true);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddProjectWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _projects.Add(new ProjectInfo
            {
                Name = dialog.ProjectName,
                Repo = dialog.RepoPath
            });
            SaveAndRefreshProjects();
        }
    }

    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ProjectInfo selected) return;

        var result = MessageBox.Show(
            $"Remove project \"{selected.Name}\"?",
            "Remove Project", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _projects.Remove(selected);
            SaveAndRefreshProjects();
        }
    }

    private void BrowseProjectRepo_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ProjectInfo selected) return;

        var dialog = new OpenFolderDialog
        {
            Title = $"Select repo folder for {selected.Name}"
        };

        if (!string.IsNullOrEmpty(selected.Repo) && System.IO.Directory.Exists(selected.Repo))
            dialog.InitialDirectory = selected.Repo;

        if (dialog.ShowDialog(this) == true)
        {
            selected.Repo = dialog.FolderName;
            SaveAndRefreshProjects();
        }
    }

    private void SaveAndRefreshProjects()
    {
        _projectService.Save(_projects);
        ProjectList.ItemsSource = null;
        ProjectList.ItemsSource = _projects;
        SettingsChanged?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
