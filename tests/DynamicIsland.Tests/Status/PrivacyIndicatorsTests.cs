using DynamicIsland.Core.Status;

namespace DynamicIsland.Tests.Status;

public sealed class PrivacyIndicatorsTests
{
    private static PrivacyStatus Status(bool camera = false, bool mic = false, bool share = false) =>
        new(camera ? ["Zoom"] : [], mic ? ["Discord"] : [], share ? ["Zoom"] : []);

    [Fact]
    public void Nothing_in_use_shows_nothing()
    {
        Assert.Empty(PrivacyIndicators.Active(PrivacyStatus.Idle));
        Assert.Null(PrivacyIndicators.Primary(PrivacyStatus.Idle));
    }

    [Fact]
    public void Screen_share_beats_camera_which_beats_microphone()
    {
        Assert.Equal([PrivacyIndicator.ScreenShare, PrivacyIndicator.Camera, PrivacyIndicator.Microphone],
            PrivacyIndicators.Active(Status(camera: true, mic: true, share: true)));
        Assert.Equal(PrivacyIndicator.Camera, PrivacyIndicators.Primary(Status(camera: true, mic: true)));
        Assert.Equal(PrivacyIndicator.Microphone, PrivacyIndicators.Primary(Status(mic: true)));
    }

    [Theory]
    [InlineData(PrivacyIndicator.ScreenShare, "Brush.Red")]
    [InlineData(PrivacyIndicator.Camera, "Brush.Red")]
    [InlineData(PrivacyIndicator.Microphone, "Brush.Orange")]
    public void Colours_follow_iOS(PrivacyIndicator indicator, string brush)
    {
        Assert.Equal(brush, PrivacyIndicators.BrushKey(indicator));
    }

    [Fact]
    public void Live_microphone_sessions_win_over_consent_store()
    {
        // ConsentStore still says Discord (stop time not written yet), but no stream is open: off.
        Assert.Empty(MicrophoneSignal.Merge([], ["Discord"]));
        Assert.Equal(["Telegram"], MicrophoneSignal.Merge(["Telegram", "telegram"], []));
        Assert.Equal(["Discord"], MicrophoneSignal.Merge(null, ["Discord"]));
    }

    [Theory]
    [InlineData("chrome", "Chrome_WidgetWin_1", "meet.google.com is sharing your screen.", "Google Chrome")]
    [InlineData("msedge", "Chrome_WidgetWin_1", "teams.microsoft.com is sharing a window.", "Microsoft Edge")]
    [InlineData("browser", "Chrome_WidgetWin_1", "telemost.yandex.ru показывает ваш экран", "Яндекс Браузер")]
    [InlineData("Zoom", "ZPToolBarParentWndClass", "", "Zoom")]
    [InlineData("ms-teams", "TeamsWebView", "Sharing control bar | Microsoft Teams", "Microsoft Teams")]
    public void Sharing_windows_are_recognised(string process, string cls, string title, string app)
    {
        Assert.Equal(app, ScreenShareSignatures.Match(process, cls, title));
    }

    [Theory]
    [InlineData("chrome", "Chrome_WidgetWin_1", "How to share your screen - Google Search")]
    [InlineData("notepad", "Notepad", "is sharing your screen")]
    [InlineData("Zoom", "ConfMultiTabContentWndClass", "Zoom Meeting")]
    public void Ordinary_windows_are_not_sharing(string process, string cls, string title)
    {
        Assert.Null(ScreenShareSignatures.Match(process, cls, title));
    }
}
