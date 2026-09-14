using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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
    private UpdateService? _updateService;

    // Quiet update-check cadence (VS Code pattern, same as the MG Manager Hub):
    // tick every 5 minutes so a long sleep can't skip a due check, but do one
    // real check per hour at most, give up silently offline, and stop for good
    // once an update is found.
    private static readonly TimeSpan UpdateCheckEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan UpdateTickEvery = TimeSpan.FromMinutes(5);
    private DispatcherTimer? _updateTimer;
    private DateTime _lastUpdateCheck = DateTime.MinValue;
    private bool _updateRunning;

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
        _updateService = new UpdateService(BaseDir);

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

        // Quiet self-update check: first tick right away, then on the cadence above.
        _updateTimer = new DispatcherTimer { Interval = UpdateTickEvery };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdateQuietlyAsync(mainVm);
        _updateTimer.Start();
        _ = CheckForUpdateQuietlyAsync(mainVm);
    }

    // The ⋮ button on the selected note (NoteDetailTemplate) opens its context
    // menu on a plain left click, so the same NoteActionsMenu serves both
    // right-click and the button.
    private void NoteMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button || button.ContextMenu is not { } menu) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>The app's one updater instance (Settings and the header link share it).</summary>
    public UpdateService Updater => _updateService ??= new UpdateService(BaseDir);

    private MainViewModel? MainVm => _mainPanel?.DataContext as MainViewModel;

    private async Task CheckForUpdateQuietlyAsync(MainViewModel mainVm)
    {
        if (mainVm.UpdateAvailableVersion != null) return; // found one; stop checking
        if (DateTime.UtcNow - _lastUpdateCheck < UpdateCheckEvery) return;
        await CheckForUpdateAsync();
    }

    /// <summary>
    /// The one update check. Every caller (hourly quiet tick, Settings button)
    /// goes through here, so whoever finds a newer release lights the header
    /// link for everyone: a manual check in Settings must never know something
    /// the header does not.
    /// </summary>
    public async Task<Helpers.UpdateCheckResult> CheckForUpdateAsync()
    {
        _lastUpdateCheck = DateTime.UtcNow;
        var res = await Updater.CheckAsync();
        if (res.Ok && res.Latest != null && MainVm is { } vm)
            vm.UpdateAvailableVersion = res.UpToDate ? null : res.Latest.ToString();
        return res;
    }

    /// <summary>
    /// Downloads and launches the latest installer. One run at a time; a second
    /// click while a download is in flight is ignored. <paramref name="status"/>
    /// receives progress text for whichever face (header link, Settings) asked.
    /// On success this process is about to be killed by the installer, which
    /// relaunches the new version afterwards, so session state is persisted first.
    /// </summary>
    public async Task<(bool Launched, string Message)> RunUpdateAsync(Action<string>? status = null)
    {
        if (_updateRunning) return (false, "An update is already in progress.");
        _updateRunning = true;
        try
        {
            status?.Invoke("Downloading…");
            var progress = new Progress<(long Got, long? Total)>(p =>
            {
                var mb = (p.Got / 1048576.0).ToString("0.0");
                status?.Invoke(p.Total is { } t && t > 0
                    ? $"Downloading… {p.Got * 100 / t}%"
                    : $"Downloading… {mb} MB");
            });

            // The installer force-kills us (tray apps hide on close, so it cannot
            // ask nicely). Flush edits and window geometry now, exactly as OnExit would.
            PersistSessionState();

            var result = await Updater.ApplyAsync(progress);
            status?.Invoke(result.Message);
            return result;
        }
        finally
        {
            _updateRunning = false;
        }
    }

    /// <summary>Everything OnExit saves, callable before an external kill.</summary>
    private void PersistSessionState()
    {
        (_mainPanel?.DataContext as MainViewModel)?.CommitSplitEdit();

        if (_mainPanel != null && _settingsService != null)
        {
            var settings = _settingsService.Load();
            settings.PanelPosition = _mainPanel.GetPersistedGeometry();
            var splitWidth = _mainPanel.GetSplitListWidth();
            if (splitWidth > 0)
                settings.SplitListWidth = splitWidth;
            _settingsService.Save(settings);
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
        // Final safety net: flush any in-place split-detail edit and remember
        // where the panel and split divider live for next launch.
        _updateTimer?.Stop();
        PersistSessionState();

        _activationSignal?.Dispose();
        _trayIcon?.Dispose();
        _noteService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
