using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DynamicIsland.Modules.Translator.Views;

public partial class TranslatorPage : UserControl
{
    public TranslatorPage() => InitializeComponent();

    private void OnInputPressed(object sender, MouseButtonEventArgs e)
    {
        (DataContext as TranslatorViewModel)?.SetTyping(true);
        ((TextBox)sender).Focus();
    }

    private void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e) => (DataContext as TranslatorViewModel)?.SetTyping(true);

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not TextBox)
        {
            (DataContext as TranslatorViewModel)?.SetTyping(false);
        }
    }

    private void OnHistory(object sender, RoutedEventArgs e)
    {
        if (DataContext is TranslatorViewModel vm) { TranslationHistoryWindow.ShowFor(vm, Window.GetWindow(this)); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (DataContext is TranslatorViewModel { Output.Length: > 0 } vm)
        {
            try
            {
                Clipboard.SetText(vm.Output);
            }
            catch (ExternalException)
            {
                // Another application owns the clipboard; the result remains available for selection.
            }
        }
    }
}
