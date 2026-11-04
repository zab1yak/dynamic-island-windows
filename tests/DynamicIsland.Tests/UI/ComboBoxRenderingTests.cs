using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland.Tests.UI;

public sealed class ComboBoxRenderingTests
{
    private static readonly string[] RequiredIcons = ["Settings", "Mail", "Telegram"];
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
            foreach (var name in RequiredIcons)
            {
                var geometry = Assert.IsAssignableFrom<Geometry>(icons["Icon." + name]);
                Assert.False(geometry.Bounds.IsEmpty);
                Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0);
            }
        });
    }

    [Fact]
    public void Every_lucide_geometry_stays_inside_its_design_grid()
    {
        OnSta(() =>
        {
            var icons = (ResourceDictionary)Application.LoadComponent(
                new Uri("/DynamicIsland.UI;component/Icons/Lucide.xaml", UriKind.Relative));
            foreach (var key in icons.Keys.OfType<string>())
            {
                var geometry = Assert.IsAssignableFrom<Geometry>(icons[key]);
                var bounds = geometry.Bounds;
                Assert.False(bounds.IsEmpty);
                Assert.True(bounds.Left >= -0.05 && bounds.Top >= -0.05 && bounds.Right <= 24.05 && bounds.Bottom <= 24.05,
                    $"{key} escapes the 24x24 grid: {bounds}");
            }

            // Bounds alone cannot catch the distortion caused by making ALL implicit
            // moveto pairs absolute: the second stroke must go from(6,6) to(18,18).
            var cross = ((Geometry)icons["Icon.Close"]).GetFlattenedPathGeometry();
            Assert.Equal(2, cross.Figures.Count);
            Assert.Equal(new Point(6, 6), cross.Figures[1].StartPoint);
            var stroke = new PathGeometry();
            stroke.Figures.Add(cross.Figures[1]);
            stroke.GetPointAtFractionLength(1, out var end, out _);
            Assert.Equal(new Point(18, 18), end);
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
