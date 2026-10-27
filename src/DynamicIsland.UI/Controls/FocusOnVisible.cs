using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DynamicIsland.UI.Controls;

/// <summary>Attached behaviour: a text box takes keyboard focus (and selects its text) as soon as it becomes visible.</summary>
public static class FocusOnVisible
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusOnVisible), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;
        if ((bool)e.NewValue)
        {
            element.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UIElement { IsVisible: true } element)
        {
            // After the window became activatable (input priority runs after the activation message).
            element.Dispatcher.BeginInvoke(() =>
            {
                element.Focus();
                (element as TextBox)?.SelectAll();
            }, DispatcherPriority.Input);
        }
    }
}
