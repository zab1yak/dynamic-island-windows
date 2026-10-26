using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.UI.Input;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.Modules.Shelf.Views;

/// <summary>
/// Shelf page view: click opens, Ctrl/Shift+click selects, dragging an item out puts real files
/// on the drag (<c>FileDrop</c>) so any folder, app or messenger accepts them.
/// </summary>
public partial class ShelfPage : UserControl
{
    private Point? _pressAt;
    private ShelfItemViewModel? _pressed;

    public ShelfPage()
    {
        InitializeComponent();
        List.PreviewMouseLeftButtonDown += OnPress;
        List.PreviewMouseMove += OnMove;
        List.PreviewMouseLeftButtonUp += OnRelease;
        List.ContextMenuOpening += OnContextMenuOpening;
        PreviewKeyDown += OnKeyDown;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ShelfViewModel old)
            {
                old.PasteRequested -= OnPasteRequested;
            }

            if (e.NewValue is ShelfViewModel vm)
            {
                vm.PasteRequested += OnPasteRequested;
            }
        };
        List.ContextMenu = new ContextMenu();
    }

    private ShelfViewModel? ViewModel => DataContext as ShelfViewModel;

    private static ShelfItemViewModel? ItemAt(object source) =>
        (source as FrameworkElement)?.DataContext as ShelfItemViewModel;

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = ItemAt(e.OriginalSource);
        _pressAt = _pressed is null ? null : e.GetPosition(this);
        if (_pressed is not null && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Keep the selection for dragging several items; plain click is handled on release.
            e.Handled = _pressed.IsSelected;
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } start || _pressed is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var items = ViewModel?.Items.Where(i => i.IsSelected).ToList() ?? [];
        if (!items.Contains(_pressed))
        {
            items = [_pressed];
        }

        _pressAt = null;
        _pressed = null;
        var data = new DataObject(DataFormats.FileDrop, items.Select(i => i.Path).ToArray());
        DragDrop.DoDragDrop(List, data, DragDropEffects.Copy);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_pressed is { } item && _pressAt is not null && Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel?.OpenCommand.Execute(item);
            e.Handled = true;
        }

        _pressAt = null;
        _pressed = null;
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = ItemAt(e.OriginalSource);
        if (item is null || ViewModel is not { } vm)
        {
            e.Handled = true;
            return;
        }

        var menu = List.ContextMenu!;
        menu.Items.Clear();
        menu.Items.Add(MenuItem("Shelf.Open", () => vm.OpenCommand.Execute(item)));
        menu.Items.Add(MenuItem("Shelf.Reveal", () => vm.RevealCommand.Execute(item)));
        menu.Items.Add(MenuItem("Shelf.CopyPath", () => vm.CopyPathCommand.Execute(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Shelf.Remove", () => vm.RemoveCommand.Execute(item)));
        menu.Items.Add(MenuItem("Shelf.ClearAll", () => vm.ClearAllCommand.Execute(null)));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnPasteRequested(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && ViewModel is { } vm)
        {
            vm.RemoveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPasteRequested(object? sender, EventArgs e)
    {
        var payload = DropPayloadReader.ReadClipboard();
        if (!payload.IsEmpty && ViewModel is { } vm)
        {
            _ = vm.HandleDropAsync(payload, CancellationToken.None);
        }
    }

    private static MenuItem MenuItem(string key, Action action)
    {
        var item = new MenuItem { Header = Loc.Instance[key] };
        item.Click += (_, _) => action();
        return item;
    }
}
