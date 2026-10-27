using DynamicIsland.Core.Status;

namespace DynamicIsland.Tests.Status;

public sealed class BrowserTitlesTests
{
    [Theory]
    [InlineData("chrome", "Lo-fi beats - YouTube - Google Chrome", "Lo-fi beats - YouTube")]
    [InlineData("chrome.exe", "Inbox (3) - Gmail - Google Chrome", "Inbox (3) - Gmail")]
    [InlineData("msedge", "Docs - Work - Microsoft​ Edge", "Docs - Work")]
    [InlineData("msedge", "Bing and 4 more pages - Profile 1 - Microsoft Edge", "Bing")]
    [InlineData("firefox", "GitHub — Mozilla Firefox", "GitHub")]
    [InlineData("browser", "Яндекс Музыка — Яндекс Браузер", "Яндекс Музыка")]
    [InlineData("opera", "News - Opera", "News")]
    public void Browser_suffix_is_removed(string process, string title, string expected)
    {
        Assert.Equal(expected, BrowserTitles.TabTitle(process, title));
    }

    [Theory]
    [InlineData("notepad", "notes.txt - Notepad")]
    [InlineData("Telegram", "Telegram")]
    [InlineData(null, "x")]
    [InlineData("chrome", "")]
    [InlineData("chrome", "Google Chrome")]
    public void Non_browsers_and_empty_titles_give_nothing(string? process, string title)
    {
        Assert.Null(BrowserTitles.TabTitle(process, title));
    }

    [Fact]
    public void Long_titles_are_cut_and_control_characters_removed()
    {
        var title = BrowserTitles.TabTitle("chrome", new string('a', 500) + "\u0007 - Google Chrome");
        Assert.NotNull(title);
        Assert.True(title.Length <= BrowserTitles.MaxLength + 1);
        Assert.DoesNotContain('\u0007', title);
    }
}
