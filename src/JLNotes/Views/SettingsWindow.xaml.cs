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
        NotesFolderText.Text = AppPaths.DataDir;

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

    /// <summary>Settings face of NotesFolder.Change: pick a folder, say plainly what will
    /// happen to the notes, then App adds them there, switches and restarts.</summary>
    private void ChangeNotesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;
        var current = AppPaths.DataDir;
        var dialog = new OpenFolderDialog
        {
            Title = "Pick the folder for your notes (a Dropbox folder shares them between PCs)",
            InitialDirectory = current
        };
        if (dialog.ShowDialog(this) != true) return;

        var target = dialog.FolderName;
        if (NotesFolder.ProblemWith(AppPaths.LocalDir, current, target) is { } problem)
        {
            MessageBox.Show(this, problem, "Notes folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, NotesFolder.WhatChangeWillDo(current, target), "Notes folder",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            app.ChangeNotesFolder(target, this);
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
            ChangeProjects(() => _projectService.Add(new ProjectInfo
            {
                Name = dialog.ProjectName,
                Repo = dialog.RepoPath
            }));
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
            ChangeProjects(() => _projectService.Remove(selected.Name));
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
            ChangeProjects(() => _projectService.SetRepo(selected.Name, dialog.FolderName));
        }
    }

    /// <summary>One change to the shared project list. ProjectService refuses to write
    /// over a list another PC is saving right now; then the user is told to try again.</summary>
    private void ChangeProjects(Action change)
    {
        try
        {
            change();
        }
        catch (System.IO.IOException ex)
        {
            MessageBox.Show(this, $"Nothing was changed.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Projects", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        RefreshProjects();
    }

    /// <summary>Shows the project list as it is now. Changes go through ProjectService one
    /// at a time, so a project another PC added while this window was open is kept.</summary>
    private void RefreshProjects()
    {
        _projects = _projectService.Load();
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
