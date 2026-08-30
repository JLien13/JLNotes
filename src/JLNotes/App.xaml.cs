using System.IO;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using JLNotes.Services;
using JLNotes.ViewModels;
using JLNotes.Views;

namespace JLNotes;

public partial class App : Application
{
    private const string ActivationEventName = "JLNotes_Activate";

    private static Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationSignal;
    private TaskbarIcon? _trayIcon;
    private MainPanelWindow? _mainPanel;
    private NoteService? _noteService;
    private SettingsService? _settingsService;
    private ProjectService? _projectService;

    private static readonly string BaseDir = AppPaths.BaseDir;
    private static readonly string LegacyDir = AppPaths.LegacyDir;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "JLNotes_SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            // Hand off to the running instance: ask it to show its panel, then
            // leave quietly. (--minimized relaunches stay silent.)
            if (!e.Args.Contains("--minimized"))
            {
                try { EventWaitHandle.OpenExisting(ActivationEventName).Set(); }
                catch (WaitHandleCannotBeOpenedException) { /* first instance still starting */ }
            }
            // We never acquired ownership, so OnExit must not ReleaseMutex.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // Migrate from legacy directory if needed
        if (!Directory.Exists(BaseDir) && Directory.Exists(LegacyDir))
        {
            try { CopyDirectory(LegacyDir, BaseDir); }
            catch { /* fall through to create fresh */ }
        }

        // Ensure data directory exists
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(AppPaths.NotesDir);

        // First-run: create empty projects.json if it doesn't exist
        var projectsPath = Path.Combine(BaseDir, "projects.json");
        var isFirstRun = !File.Exists(projectsPath);
        if (isFirstRun)
            File.WriteAllText(projectsPath, "[]");

        // Create services
        _noteService = new NoteService(AppPaths.NotesDir);
        _settingsService = new SettingsService(BaseDir);
        _projectService = new ProjectService(BaseDir);

        // Load settings and apply theme
        var settings = _settingsService.Load();
        ApplyTheme(settings.Theme);

        // Create main VM
        var mainVm = new MainViewModel(_noteService, _projectService, _settingsService);

        // Create main panel (hidden initially) where the last session left it
        _mainPanel = new MainPanelWindow { DataContext = mainVm };
        _mainPanel.ApplyPersistedGeometry(settings.PanelPosition);
        _mainPanel.ApplySplitListWidth(settings.SplitListWidth);

        // A second launch of the exe signals this event instead of starting up.
        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        new Thread(ActivationListenerLoop) { IsBackground = true }.Start();

        // Set up tray icon
        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/tray.ico")),
            ToolTipText = "JL Notes",
            ContextMenu = CreateTrayMenu(),
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => TogglePanel();

        // Show the panel on startup -- unless launched by the Windows Run key
        // (--minimized), where the expected behavior is to sit quietly in the tray.
        if (!e.Args.Contains("--minimized"))
        {
            _mainPanel.Show();
            _mainPanel.Activate();
        }
    }

    private void ActivationListenerLoop()
    {
        while (_activationSignal is { } signal)
        {
            try { signal.WaitOne(); }
            catch (ObjectDisposedException) { return; }

            Dispatcher.BeginInvoke(() =>
            {
                if (_mainPanel == null) return;
                _mainPanel.Show();
                if (_mainPanel.WindowState == WindowState.Minimized)
                    _mainPanel.WindowState = WindowState.Normal;
                _mainPanel.Activate();
            });
        }
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
        // Final safety net: flush any in-place split-detail edit before teardown.
        (_mainPanel?.DataContext as MainViewModel)?.CommitSplitEdit();

        // Remember where the panel and split divider live for next launch.
        if (_mainPanel != null && _settingsService != null)
        {
            var settings = _settingsService.Load();
            settings.PanelPosition = _mainPanel.GetPersistedGeometry();
            var splitWidth = _mainPanel.GetSplitListWidth();
            if (splitWidth > 0)
                settings.SplitListWidth = splitWidth;
            _settingsService.Save(settings);
        }

        _activationSignal?.Dispose();
        _trayIcon?.Dispose();
        _noteService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
