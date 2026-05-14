using System.Windows;
using System.Windows.Input;
using JLNotes.Models;
using JLNotes.ViewModels;
using JLNotes.Services;

namespace JLNotes.Views;

public partial class MainPanelWindow : Window
{
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;
    private bool _isMaximized;
    private bool _headerDragging;

    public MainPanelWindow()
    {
        InitializeComponent();
        PositionTopRight();
        DataContextChanged += (_, _) => RefreshCloseButtonTooltip();
        Loaded += (_, _) => RefreshCloseButtonTooltip();
    }

    private void RefreshCloseButtonTooltip()
    {
        if (DataContext is not MainViewModel mainVm) return;
        var behavior = mainVm.SettingsService.Load().CloseBehavior;
        CloseButton.ToolTip = behavior == "quit" ? "Quit JL Notes" : "Close to tray";
    }

    private void PositionTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        const double padding = 16;
        Left = workArea.Right - Width - padding;
        Top = workArea.Top + padding;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            _headerDragging = true;
            DragMove();
            _headerDragging = false;
        }
    }

    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (_headerDragging && _isMaximized && e.LeftButton == MouseButtonState.Pressed)
        {
            var mousePos = PointToScreen(e.GetPosition(this));
            _isMaximized = false;
            Left = mousePos.X - _restoreWidth / 2;
            Top = mousePos.Y - 15;
            Width = _restoreWidth;
            Height = _restoreHeight;
            UpdateMaximizeButton();
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        // If Windows itself maximized us (Win+Up etc.), drop back to Normal so we own the geometry.
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        var workArea = SystemParameters.WorkArea;

        // Detect maximized state from actual geometry, not a tracked flag — survives drag/OS resize desync.
        bool atWorkAreaSize =
            Math.Abs(Left - workArea.Left) < 1 &&
            Math.Abs(Top - workArea.Top) < 1 &&
            Math.Abs(Width - workArea.Width) < 1 &&
            Math.Abs(Height - workArea.Height) < 1;

        if (!atWorkAreaSize)
        {
            _restoreLeft = Left;
            _restoreTop = Top;
            _restoreWidth = Width;
            _restoreHeight = Height;

            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            _isMaximized = true;
        }
        else
        {
            // Guard against uninitialized restore values (e.g., first toggle after Win+Up).
            if (_restoreWidth < MinWidth || _restoreHeight < MinHeight)
            {
                _restoreWidth = 380;
                _restoreHeight = 550;
                _restoreLeft = workArea.Right - _restoreWidth - 16;
                _restoreTop = workArea.Top + 16;
            }

            Left = _restoreLeft;
            Top = _restoreTop;
            Width = _restoreWidth;
            Height = _restoreHeight;
            _isMaximized = false;
        }
        UpdateMaximizeButton();
    }

    private void UpdateMaximizeButton()
    {
        MaximizeButton.Content = _isMaximized ? "\U0001F5D7" : "\U0001F5D6";
        MaximizeButton.ToolTip = _isMaximized ? "Restore Down" : "Maximize";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;
        var settingsWindow = new SettingsWindow(mainVm.SettingsService, mainVm.ProjectService) { Owner = this };
        settingsWindow.SettingsChanged += () =>
        {
            mainVm.RefreshNotes();
            RefreshCloseButtonTooltip();
        };
        settingsWindow.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            var settings = mainVm.SettingsService.Load();
            if (settings.CloseBehavior == "quit")
            {
                Application.Current.Shutdown();
                return;
            }
        }
        Hide();
    }

    private void AddNote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;

        var newNote = new Note();
        var detailVm = new NoteDetailViewModel(newNote, mainVm.NoteService, isNew: true);
        var detailWindow = new NoteDetailWindow { DataContext = detailVm };
        detailVm.Saved += () =>
        {
            detailWindow.Close();
            mainVm.RefreshNotes();
        };
        detailVm.Closed += () => detailWindow.Close();
        detailWindow.Show();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.ScrollViewer sv)
        {
            // Reduce scroll speed to ~1/3 for smoother trackpad scrolling
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel mainVm) return;

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // If the search box has focus, Esc clears/blurs it instead of collapsing notes.
            if (SearchBox.IsKeyboardFocusWithin)
            {
                if (!string.IsNullOrEmpty(mainVm.SearchText))
                    mainVm.SearchText = "";
                else
                    Keyboard.ClearFocus();
            }
            else
            {
                mainVm.CollapseAll();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            mainVm.SaveExpanded();
            e.Handled = true;
        }
    }
}
