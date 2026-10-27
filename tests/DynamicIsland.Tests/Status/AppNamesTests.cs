using DynamicIsland.Core.Status;

namespace DynamicIsland.Tests.Status;

public sealed class AppNamesTests
{
    private static ConsentEntry Desktop(string path) => new(path.Replace('\\', '#'), true, 1, 0);

    private static ConsentEntry Packaged(string family) => new(family, false, 1, 0);

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Discord\app-1.0\Discord.exe", "Discord")]
    [InlineData(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe", "OBS Studio")]
    [InlineData(@"C:\Users\me\AppData\Local\Yandex\YandexBrowser\Application\browser.exe", "Яндекс Браузер")]
    [InlineData(@"C:\Program Files\Zoom\bin\Zoom.exe", "Zoom")]
    public void Known_desktop_apps_get_their_product_name(string path, string expected)
    {
        Assert.Equal(expected, AppNames.Resolve(Desktop(path)));
    }

    [Fact]
    public void Unknown_desktop_app_uses_its_version_info()
    {
        var name = AppNames.Resolve(Desktop(@"D:\Tools\rec2.exe"), productName: _ => "Super Recorder");
        Assert.Equal("Super Recorder", name);
    }

    [Fact]
    public void Unknown_desktop_app_without_version_info_uses_the_file_name()
    {
        Assert.Equal("rec2", AppNames.Resolve(Desktop(@"D:\Tools\rec2.exe"), productName: _ => null));
    }

    [Theory]
    [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy")]
    [InlineData("Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy")]
    [InlineData("MicrosoftWindows.Client.CBS_cw5n1h2txyewy")]
    public void Windows_components_are_system_apps(string family)
    {
        Assert.Equal(AppNames.SystemApp, AppNames.Resolve(Packaged(family)));
    }

    [Fact]
    public void Executables_in_the_Windows_folder_are_system_apps()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "svchost.exe");
        Assert.Equal(AppNames.SystemApp, AppNames.Resolve(Desktop(path)));
    }

    [Theory]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "Windows Camera")]
    [InlineData("MSTeams_8wekyb3d8bbwe", "Microsoft Teams")]
    [InlineData("TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm", "Telegram")]
    public void Known_store_apps(string family, string expected)
    {
        Assert.Equal(expected, AppNames.Resolve(Packaged(family)));
    }

    [Fact]
    public void Unknown_store_app_uses_the_package_display_name()
    {
        Assert.Equal("Cool Meet", AppNames.Resolve(Packaged("Contoso.CoolMeet_abc123"), packageName: _ => "Cool Meet"));
        Assert.Equal("CoolMeet", AppNames.Resolve(Packaged("Contoso.CoolMeet_abc123"), packageName: _ => null));
    }
}
