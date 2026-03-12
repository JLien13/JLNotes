using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JLNotes.ViewModels;

namespace JLNotes.Behaviors;

public static class RichTextBoxBehavior
{
    private static Popup? _activePopup;
    private static bool _isProcessingMove;
    private static bool _hoverSuppressed;
    private static DateTime _lastClickTime = DateTime.MinValue;
    private static string? _lastClickedFile;

    #region Document attached property

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.RegisterAttached(
            "Document",
            typeof(FlowDocument),
            typeof(RichTextBoxBehavior),
            new PropertyMetadata(null, OnDocumentChanged));

    public static FlowDocument? GetDocument(DependencyObject obj) =>
        (FlowDocument?)obj.GetValue(DocumentProperty);

    public static void SetDocument(DependencyObject obj, FlowDocument? value) =>
        obj.SetValue(DocumentProperty, value);

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox rtb) return;

        // Clean up
        KillPopup();

        if (e.NewValue is FlowDocument doc)
        {
            rtb.Document = doc;

            rtb.RemoveHandler(UIElement.DropEvent, (DragEventHandler)OnDrop);
            rtb.RemoveHandler(UIElement.DragOverEvent, (DragEventHandler)OnDragOver);
            rtb.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
            rtb.MouseMove -= OnMouseMove;
            rtb.MouseLeave -= OnMouseLeave;

            rtb.AddHandler(UIElement.DropEvent, (DragEventHandler)OnDrop, true);
            rtb.AddHandler(UIElement.DragOverEvent, (DragEventHandler)OnDragOver, true);
            rtb.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
            rtb.MouseMove += OnMouseMove;
            rtb.MouseLeave += OnMouseLeave;
        }
        else
        {
            rtb.Document = new FlowDocument();
        }
    }

    #endregion

    #region Drag-drop

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is not RichTextBox rtb) return;
            if (rtb.DataContext is not NoteItemViewModel vm) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                vm.HandleImageDrop(files, rtb);
                e.Handled = true;
            }
        }
        catch { }
    }

    #endregion

    #region Click to open

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            // Always kill popup and suppress hover on any click
            KillPopup();
            _hoverSuppressed = true;

            if (sender is not RichTextBox rtb) return;

            var textBlock = SafeHitTest(rtb, e.GetPosition(rtb));
            if (textBlock == null) return;

            var filePath = GetAttachmentFilePath(rtb, textBlock);
            if (filePath == null || !File.Exists(filePath)) return;

            // Debounce: same file within 2 seconds
            var now = DateTime.UtcNow;
            if (filePath == _lastClickedFile && (now - _lastClickTime).TotalSeconds < 2)
            {
                e.Handled = true;
                return;
            }
            _lastClickedFile = filePath;
            _lastClickTime = now;

            // Flash: dim briefly
            var originalBrush = textBlock.Foreground;
            textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA));
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            timer.Tick += (_, _) =>
            {
                try { textBlock.Foreground = originalBrush; } catch { }
                timer.Stop();
                // Re-enable hover after flash completes
                _hoverSuppressed = false;
            };
            timer.Start();

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            e.Handled = true;
        }
        catch { }
    }

    #endregion

    #region Hover to preview

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isProcessingMove || _hoverSuppressed) return;
        _isProcessingMove = true;

        try
        {
            if (sender is not RichTextBox rtb) { return; }

            var textBlock = SafeHitTest(rtb, e.GetPosition(rtb));

            if (textBlock == null)
            {
                KillPopup();
                return;
            }

            // Already showing popup for this element
            if (_activePopup != null && _activePopup.Tag == textBlock) return;

            KillPopup();

            var filePath = GetAttachmentFilePath(rtb, textBlock);
            if (filePath == null || !File.Exists(filePath)) return;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                MaxWidth = 300,
                MaxHeight = 300
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x4a)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Child = image
            };

            _activePopup = new Popup
            {
                Child = border,
                PlacementTarget = rtb,
                Placement = PlacementMode.Mouse,
                AllowsTransparency = true,
                IsHitTestVisible = false,
                StaysOpen = true,
                Tag = textBlock,
                IsOpen = true
            };
        }
        catch
        {
            KillPopup();
        }
        finally
        {
            _isProcessingMove = false;
        }
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        try { KillPopup(); } catch { }
    }

    #endregion

    #region Popup management

    private static void KillPopup()
    {
        try
        {
            if (_activePopup != null)
            {
                _activePopup.IsOpen = false;
                _activePopup.Child = null;
                _activePopup = null;
            }
        }
        catch
        {
            _activePopup = null;
        }
    }

    #endregion

    #region Hit testing

    private static TextBlock? SafeHitTest(RichTextBox rtb, Point position)
    {
        try
        {
            var result = VisualTreeHelper.HitTest(rtb, position);
            if (result?.VisualHit == null) return null;

            DependencyObject? current = result.VisualHit;
            while (current != null && current != rtb)
            {
                if (current is TextBlock tb && tb.Tag is string)
                    return tb;
                current = VisualTreeHelper.GetParent(current);
            }
        }
        catch { }
        return null;
    }

    private static string? GetAttachmentFilePath(RichTextBox rtb, TextBlock textBlock)
    {
        try
        {
            if (textBlock.Tag is not string filename) return null;
            if (rtb.DataContext is not NoteItemViewModel vm) return null;
            return Path.Combine(vm.Note.GetAttachmentsDir(), filename);
        }
        catch { return null; }
    }

    #endregion

    #region Upload button

    public static readonly DependencyProperty IsUploadButtonProperty =
        DependencyProperty.RegisterAttached(
            "IsUploadButton",
            typeof(bool),
            typeof(RichTextBoxBehavior),
            new PropertyMetadata(false, OnIsUploadButtonChanged));

    public static bool GetIsUploadButton(DependencyObject obj) =>
        (bool)obj.GetValue(IsUploadButtonProperty);

    public static void SetIsUploadButton(DependencyObject obj, bool value) =>
        obj.SetValue(IsUploadButtonProperty, value);

    private static void OnIsUploadButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;
        if (e.NewValue is true)
            button.Click += OnUploadClick;
        else
            button.Click -= OnUploadClick;
    }

    private static void OnUploadClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button) return;
            if (button.DataContext is not NoteItemViewModel vm) return;

            if (button.Parent is FrameworkElement grid &&
                grid.Parent is FrameworkElement container)
            {
                var rtb = FindChild<RichTextBox>(container);
                if (rtb != null)
                    vm.HandleImageUpload(rtb);
            }
        }
        catch { }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        try
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
        }
        catch { }
        return null;
    }

    #endregion
}
