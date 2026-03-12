using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JLNotes.ViewModels;

namespace JLNotes.Behaviors;

public static class NoteDragDrop
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(NoteDragDrop),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static Point _startPoint;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += OnMouseDown;
            element.PreviewMouseMove += OnMouseMove;
            element.AllowDrop = true;
            element.DragOver += OnDragOver;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= OnMouseDown;
            element.PreviewMouseMove -= OnMouseMove;
            element.DragOver -= OnDragOver;
            element.DragLeave -= OnDragLeave;
            element.Drop -= OnDrop;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        // Don't start drag from interactive controls
        if (IsInteractiveSource(e.OriginalSource as DependencyObject, sender as DependencyObject))
            return;

        var pos = e.GetPosition(null);
        var diff = _startPoint - pos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is FrameworkElement element && element.DataContext is NoteItemViewModel vm)
            {
                DragDrop.DoDragDrop(element, vm, DragDropEffects.Move);
            }
        }
    }

    private static bool IsInteractiveSource(DependencyObject? source, DependencyObject? root)
    {
        while (source != null && source != root)
        {
            if (source is CheckBox or TextBox or ComboBox or Button)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element &&
            e.Data.GetDataPresent(typeof(NoteItemViewModel)))
        {
            var source = e.Data.GetData(typeof(NoteItemViewModel)) as NoteItemViewModel;
            var target = element.DataContext as NoteItemViewModel;
            if (source != null && target != null && source != target && source.Priority == target.Priority)
            {
                e.Effects = DragDropEffects.Move;
                element.Opacity = 0.6;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element)
            element.Opacity = 1.0;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Opacity = 1.0;

            if (element.DataContext is NoteItemViewModel target &&
                e.Data.GetData(typeof(NoteItemViewModel)) is NoteItemViewModel source &&
                source != target && source.Priority == target.Priority)
            {
                target.AcceptDrop(source);
            }
        }
        e.Handled = true;
    }
}
