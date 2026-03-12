using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using JLNotes.Services;
using JLNotes.ViewModels;
using JLNotes.Views;

namespace JLNotes;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MainPanelWindow? _mainPanel;
    private NoteService? _noteService;
    private SettingsService? _settingsService;
    private ProjectService? _projectService;

    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".jlnotes");

    private static readonly string LegacyDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude-notes");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Migrate from legacy directory if needed
        if (!Directory.Exists(BaseDir) && Directory.Exists(LegacyDir))
        {
            try { CopyDirectory(LegacyDir, BaseDir); }
            catch { /* fall through to create fresh */ }
        }

        // Ensure data directory exists
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(Path.Combine(BaseDir, "notes"));

        // First-run: create empty projects.json if it doesn't exist
        var projectsPath = Path.Combine(BaseDir, "projects.json");
        var isFirstRun = !File.Exists(projectsPath);
        if (isFirstRun)
            File.WriteAllText(projectsPath, "[]");

        // Create services
        _noteService = new NoteService(Path.Combine(BaseDir, "notes"));
        _settingsService = new SettingsService(BaseDir);
        _projectService = new ProjectService(BaseDir);

        // Load settings and apply theme
        var settings = _settingsService.Load();
        ApplyTheme(settings.Theme);

        // Create main VM
        var mainVm = new MainViewModel(_noteService, _projectService, _settingsService);

        // Create main panel (hidden initially)
        _mainPanel = new MainPanelWindow { DataContext = mainVm };

        // Set up tray icon
        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/tray.ico")),
            ToolTipText = "JL Notes",
            ContextMenu = CreateTrayMenu(),
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => TogglePanel();

        // Show the panel on startup
        _mainPanel.Show();
        _mainPanel.Activate();
    }

    private void TogglePanel()
    {
        if (_mainPanel == null) return;

        if (_mainPanel.IsVisible)
            _mainPanel.Hide();
        else
        {
            _mainPanel.Show();
            _mainPanel.Activate();
        }
    }

    private System.Windows.Controls.ContextMenu CreateTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open Panel" };
        openItem.Click += (_, _) => TogglePanel();
        menu.Items.Add(openItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) =>
        {
            _trayIcon?.Dispose();
            _noteService?.Dispose();
            Shutdown();
        };
        menu.Items.Add(quitItem);

        return menu;
    }

    public void ApplyTheme(string theme)
    {
        var dict = Resources.MergedDictionaries;
        var colorsUri = theme == "light"
            ? new Uri("Resources/LightColors.xaml", UriKind.Relative)
            : new Uri("Resources/Colors.xaml", UriKind.Relative);

        // Replace the first dictionary (Colors)
        if (dict.Count > 0)
            dict[0] = new ResourceDictionary { Source = colorsUri };
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _noteService?.Dispose();
        base.OnExit(e);
    }
}
