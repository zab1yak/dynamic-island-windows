using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace DynamicIsland.Modules.Translator.Views;

public partial class TranslatorSettingsPage : UserControl
{
    public TranslatorSettingsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => ApiKey.Clear();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is TranslatorSettingsViewModel vm && vm.SaveApiKey(ApiKey.Password))
        {
            ApiKey.Clear();
        }
    }

    // These links never include the user's text or credentials.
    private void OnDocumentation(object sender, RoutedEventArgs e) => Open("https://yandex.cloud/en/docs/translate/quickstart");

    private void OnPricing(object sender, RoutedEventArgs e) => Open("https://yandex.cloud/en/docs/translate/pricing");

    private void OnTerms(object sender, RoutedEventArgs e) => Open("https://yandex.cloud/en/docs/billing/concepts/contract");

    private void OnWeb(object sender, RoutedEventArgs e) => Open("https://translate.yandex.com/");

    private static void Open(string address)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // The browser may be unavailable. No data has been sent.
        }
    }
}
