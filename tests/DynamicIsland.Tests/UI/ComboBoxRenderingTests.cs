using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland.Tests.UI;

public sealed class ComboBoxRenderingTests
{
    private sealed record PositionChoice(string Value, string Label);

    [Theory]
    [InlineData("По центру")]
    [InlineData("Center")]
    [InlineData("Русский")]
    [InlineData("English")]
    public void Display_member_path_is_used_by_the_selected_item(string label)
    {
        OnSta(() =>
        {
            var theme = (ResourceDictionary)Application.LoadComponent(
                new Uri("/DynamicIsland.UI;component/Themes/Theme.xaml", UriKind.Relative));
            var combo = new ComboBox
            {
                Width = 220,
                DisplayMemberPath = nameof(PositionChoice.Label),
                ItemsSource = new[] { new PositionChoice("center", label) },
                SelectedIndex = 0,
                Style = (Style)theme[typeof(ComboBox)],
            };
            combo.Resources.MergedDictionaries.Add(theme);
            combo.ApplyTemplate();
            combo.Measure(new Size(220, 30));
            combo.Arrange(new Rect(0, 0, 220, 30));
            combo.UpdateLayout();
            var rendered = Descendants<TextBlock>(combo).Select(text => text.Text).ToArray();
            Assert.Contains(label, rendered);
            Assert.DoesNotContain(rendered, text => text.Contains("PositionChoice", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Settings_and_support_icon_geometries_are_present_and_nonempty()
    {
        OnSta(() =>
        {
            var icons = (ResourceDictionary)Application.LoadComponent(
                new Uri("/DynamicIsland.UI;component/Icons/Lucide.xaml", UriKind.Relative));
            icons.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/DynamicIsland.UI;component/Icons/Support.xaml", UriKind.Relative)));
            foreach (var name in new[] { "Settings", "Mail", "Telegram" })
            {
                var geometry = Assert.IsAssignableFrom<Geometry>(icons["Icon." + name]);
                Assert.False(geometry.Bounds.IsEmpty);
                Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0);
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("STA rendering test failed", failure);
        }
    }
}
