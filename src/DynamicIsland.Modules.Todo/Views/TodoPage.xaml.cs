using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland.Modules.Todo.Views;

/// <summary>
/// Todo page view. Text boxes take the keyboard only while focused (the island returns to its
/// non-activating state afterwards); rows are reordered by dragging them.
/// </summary>
public partial class TodoPage : UserControl
{
    private const string DragFormat = "DynamicIsland.TodoItem";
    private Point? _pressAt;
    private TodoItemViewModel? _pressed;

    public TodoPage()
    {
        InitializeComponent();
        NewTask.IsVisibleChanged += (_, _) =>
        {
            if (NewTask.IsVisible)
            {
                // Wait until the window is activatable, then focus.
                Dispatcher.BeginInvoke(() => NewTask.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            }
        };
        // PreviewKeyDown: a multi-line TextBox would otherwise consume Enter itself.
        NewTask.PreviewKeyDown += OnNewTaskKey;
        NewTask.GotKeyboardFocus += (_, _) => ViewModel?.SetTyping(true);
        NewTask.LostKeyboardFocus += (_, _) =>
        {
            if (ViewModel is not { } vm)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.NewTitle))
            {
                vm.CancelAddCommand.Execute(null);
            }
            else
            {
                // Keep the draft visible, but give the keyboard back to Windows.
                vm.SetTyping(false);
            }
        };

        AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRenameKey), handledEventsToo: false);
        AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.NewFocus is TextBox box && !ReferenceEquals(box, NewTask))
            {
                ViewModel?.SetTyping(true);
            }
        }));
        AddHandler(UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.OldFocus is TextBox { DataContext: TodoItemViewModel row } && row.IsEditing)
            {
                ViewModel?.CommitRenameCommand.Execute(row);
                ViewModel?.SetTyping(false);
            }
        }));

        List.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressed = (e.OriginalSource as FrameworkElement)?.DataContext as TodoItemViewModel;
            _pressAt = _pressed is null || IsInsideTextBox(e.OriginalSource as DependencyObject) ? null : e.GetPosition(List);
        };
        List.PreviewMouseMove += OnMove;
        List.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        List.Drop += OnDrop;
    }

    private TodoViewModel? ViewModel => DataContext as TodoViewModel;

    private static bool IsInsideTextBox(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBox)
            {
                return true;
            }

            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnNewTaskKey(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Enter adds the task; Shift+Enter falls through and inserts a line break.
            vm.CommitAddCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelAddCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not TextBox { DataContext: TodoItemViewModel row } || ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.CommitRenameCommand.Execute(row);
            vm.SetTyping(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsEditing = false;
            vm.SetTyping(false);
            e.Handled = true;
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } start || _pressed is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(List) - start;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _pressed;
        _pressAt = null;
        _pressed = null;
        DragDrop.DoDragDrop(List, new DataObject(DragFormat, item.Id.ToString()), DragDropEffects.Move);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm || e.Data.GetData(DragFormat) is not string id)
        {
            return;
        }

        var item = vm.Items.FirstOrDefault(i => i.Id.ToString() == id);
        if (item is null)
        {
            return;
        }

        var target = e.OriginalSource as DependencyObject;
        while (target is not null and not ListBoxItem)
        {
            target = VisualTreeHelper.GetParent(target);
        }

        var index = target is ListBoxItem container ? List.ItemContainerGenerator.IndexFromContainer(container) : vm.Items.Count - 1;
        _ = vm.MoveAsync(item, index);
        e.Handled = true;
    }
}
