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

    private static readonly string LocalDir = AppPaths.LocalDir;
    private static readonly string LegacyDir = AppPaths.LegacyDir;

    // Passed to the copy started by Restart, which waits for this one to be gone.
    private const string AfterArg = "--after";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WaitForPreviousRun(e.Args);

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
        if (!Directory.Exists(LocalDir) && Directory.Exists(LegacyDir))
        {
            try { Helpers.FolderCopy.CopyMissing(LegacyDir, LocalDir); }
            catch { /* fall through to create fresh */ }
        }

        // This PC's own folder (settings) always exists; the notes folder may be elsewhere.
        Directory.CreateDirectory(LocalDir);
        _settingsService = new SettingsService(LocalDir);
        if (NotesFolder.ForThisRun(_settingsService, LocalDir, UseThisPcsFolderInstead) is not { } dataDir)
        {
            Shutdown();
            return;
        }
        AppPaths.DataDir = dataDir;
        Directory.CreateDirectory(AppPaths.NotesDir);

        // First-run: create empty projects.json if it doesn't exist
        var projectsPath = Path.Combine(AppPaths.DataDir, AppPaths.ProjectsFileName);
        var isFirstRun = !File.Exists(projectsPath);
        if (isFirstRun)
            File.WriteAllText(projectsPath, "[]");

        // Create services
        _noteService = new NoteService(AppPaths.NotesDir);
        _noteService.MigrateAttachmentFolders();
        _projectService = new ProjectService(AppPaths.DataDir);
        _updateService = new UpdateService(LocalDir);
        KeepClaudeSkillUpToDate();

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
        // Asked once the panel is on screen, never at login.
        _mainPanel.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, OfferNotesFolderBack);
        };
        if (_mainPanel.IsVisible)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, OfferNotesFolderBack);

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

    // Tag chip (TagChipTemplate, every layout): require that tag in the search.
    // Handled at preview time so a chip on a list card does not also toggle
    // the card open.
    private void TagChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not string tag) return;
        MainVm?.AddTagToSearch(tag);
        e.Handled = true;
    }

    // Split-pane tag editing (NoteDetailTemplate). The VM owns the state
    // (IsEditingTags, EditTags); these handlers only flip it and hand focus around.
    private void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteItemViewModel vm)
            vm.IsEditingTags = true;
    }

    private void TagsEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is System.Windows.Controls.TextBox box)
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }
    }

    private void TagsEditBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box || box.DataContext is not NoteItemViewModel vm) return;
        if (e.Key == System.Windows.Input.Key.Escape)
            vm.EditTags = NoteItemViewModel.FormatTags(vm.Tags); // revert, then fall through to close
        else if (e.Key != System.Windows.Input.Key.Enter)
            return;
        e.Handled = true;
        vm.IsEditingTags = false; // collapsing the box drops focus -> TagsEditBox_Done commits
    }

    private void TagsEditBox_Done(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteItemViewModel vm) return;
        vm.IsEditingTags = false;
        vm.SaveEditor();
    }

    /// <summary>After an update, the skill this app wrote into Claude's folder (Settings >
    /// Claude > Connect) is brought up to date, so it never falls behind the app.</summary>
    private static void KeepClaudeSkillUpToDate()
    {
        try
        {
            ClaudeSkill.ForThisPc().KeepUpToDate();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Claude's folder can't be read or written now. Settings > Claude says so, and why.
        }
    }

    /// <summary>The app's one updater instance (Settings and the header link share it).</summary>
    public UpdateService Updater => _updateService ??= new UpdateService(LocalDir);

    /// <summary>The notes folder picked in Settings can't be found at start: asks whether
    /// to use this PC's own folder instead. No closes JL Notes.</summary>
    private static bool UseThisPcsFolderInstead(string problem)
    {
        var nl = Environment.NewLine;
        return MessageBox.Show(
            $"{problem}{nl}{nl}Use this PC's own notes folder for now? It has only what this PC had before it " +
            "used that folder (it may be out of date, or empty), and changes made on other PCs won't show. " +
            $"Once the folder is back, JL Notes offers to go back to it, adding anything written here meanwhile.{nl}{nl}" +
            "No closes JL Notes, so you can try again later.",
            "JL Notes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Makes <paramref name="target"/> the notes folder (NotesFolder.Change: this PC's
    /// notes are added to it) and restarts, since every part of the app reads the folder
    /// once at start. Unsaved typing is saved first, so it moves too. If it fails, the
    /// notes folder stays as it was and the user is told why.
    /// </summary>
    public void ChangeNotesFolder(string target, Window? owner)
    {
        PersistSessionState();
        try
        {
            if (NotesFolder.Change(_settingsService!, LocalDir, AppPaths.DataDir, target) != NotesFolderOutcome.Unchanged)
                Restart();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var nl = Environment.NewLine;
            var text = $"The notes folder was not changed: JL Notes still uses {AppPaths.DataDir}.{nl}{nl}{ex.Message}" +
                       $"{nl}{nl}Anything already copied to {target} stays there; picking it again finishes the copy.";
            if (owner != null)
                MessageBox.Show(owner, text, "Notes folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(text, "Notes folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool _offeredNotesFolderBack;

    /// <summary>This PC fell back to its own folder at a start because the notes folder
    /// could not be found. Now that it is back, offers to go back to it.</summary>
    private void OfferNotesFolderBack()
    {
        if (_offeredNotesFolderBack || _mainPanel is not { IsVisible: true }
            || NotesFolder.FolderToGoBackTo(_settingsService!.Load(), AppPaths.DataDir) is not { } away) return;
        _offeredNotesFolderBack = true; // once a run
        var nl = Environment.NewLine;
        var answer = MessageBox.Show(_mainPanel!,
            $"Your notes folder is back:{nl}{away}{nl}{nl}Go back to it now? Notes written on this PC meanwhile " +
            "are added to it (notes deleted here meanwhile stay there), and JL Notes restarts. If Dropbox is " +
            $"still catching up on this PC, choose No and pick the folder in Settings once it is done.{nl}{nl}" +
            "No stays with this PC's own notes folder.",
            "JL Notes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            ChangeNotesFolder(away, _mainPanel);
        else
            NotesFolder.ForgetFolderToGoBackTo(_settingsService);
    }

    /// <summary>Closes and starts again. The new copy waits until this one is gone.
    /// Shutdown runs OnExit, which saves and lets go of everything.</summary>
    private void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe != null)
            System.Diagnostics.Process.Start(exe, $"{AfterArg} {Environment.ProcessId}");
        Shutdown();
    }

    private static void WaitForPreviousRun(string[] args)
    {
        var at = Array.IndexOf(args, AfterArg);
        if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out var pid)) return;
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

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

    /// <summary>Everything OnExit saves, callable before an external kill: every open
    /// editor (the split pane and any open card) and where the windows are.</summary>
    private void PersistSessionState()
    {
        MainVm?.SaveOpenEditors();

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

    /// <summary>Tray > Deleted Notes: pick a deleted note and put it back. The panel
    /// comes up so the restored note shows where it is selected.</summary>
    private void ShowDeletedNotes()
    {
        if (_mainPanel?.DataContext is not MainViewModel vm) return;
        _mainPanel.Show();
        _mainPanel.Activate();
        new HistoryWindow("Deleted notes", vm.NoteService.GetDeleted(), vm.RestoreDeleted) { Owner = _mainPanel }
            .ShowDialog();
    }

    private System.Windows.Controls.ContextMenu CreateTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open Panel" };
        openItem.Click += (_, _) => TogglePanel();
        menu.Items.Add(openItem);

        var deletedItem = new System.Windows.Controls.MenuItem { Header = "Deleted Notes..." };
        deletedItem.Click += (_, _) => ShowDeletedNotes();
        menu.Items.Add(deletedItem);

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
