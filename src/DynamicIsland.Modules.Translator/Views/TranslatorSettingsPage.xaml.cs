using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Modules.Translator.Views;

public partial class TranslatorSettingsPage : UserControl
{
    public TranslatorSettingsPage() => InitializeComponent();
    private void OnHistory(object sender, RoutedEventArgs e)
    {
        if (DataContext is TranslatorSettingsViewModel vm) { TranslationHistoryWindow.ShowFor(vm.Translator, Window.GetWindow(this)); }
    }
}
