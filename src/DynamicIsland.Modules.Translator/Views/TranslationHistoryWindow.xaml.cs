using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Modules.Translator.Views;

public partial class TranslationHistoryWindow : Window
{
    private TranslationHistoryWindow(TranslatorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.ConfirmClearHistory = false;
    }

    public static void ShowFor(TranslatorViewModel viewModel, Window? owner)
    {
        var existing = Application.Current.Windows.OfType<TranslationHistoryWindow>().FirstOrDefault(window => ReferenceEquals(window.DataContext, viewModel));
        if (existing is not null) { existing.Activate(); return; }
        var window = new TranslationHistoryWindow(viewModel) { Owner = owner };
        window.Show();
    }

    private void OnRestore(object sender, RoutedEventArgs e) => Close();
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text })
        {
            try { Clipboard.SetText(text); }
            catch (ExternalException) { /* The selectable text remains visible. */ }
        }
    }
}
