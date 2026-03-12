using System.Windows;
using System.Windows.Controls;
using JLNotes.Services;

namespace JLNotes.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private bool _initialized;

    public event Action? SettingsChanged;

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
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

        _initialized = true;
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

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
