using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core.Settings;

namespace DynamicIsland.App.Island;

/// <summary>
/// Island background (settings → «Внешний вид»): swaps the theme brushes for black, translucent black or
/// light grey with dark text, cross-fading the colours. Every colour comes from Colors.xaml.
/// </summary>
internal static class IslandTheme
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(320);

    // Brush key → (dark colour key, light colour key).
    private static readonly (string Brush, string Dark, string Light)[] Map =
    [
        ("Island.Background", "Color.Island", "Color.Light.Island"),
        ("Island.Stroke", "Color.Stroke", "Color.Light.Stroke"),
        ("Brush.Surface1", "Color.Surface1", "Color.Light.Surface1"),
        ("Brush.Surface2", "Color.Surface2", "Color.Light.Surface2"),
        ("Brush.Surface3", "Color.Surface3", "Color.Light.Surface3"),
        ("Brush.TextPrimary", "Color.TextPrimary", "Color.Light.TextPrimary"),
        ("Brush.TextSecondary", "Color.TextSecondary", "Color.Light.TextSecondary"),
        ("Brush.TextTertiary", "Color.TextTertiary", "Color.Light.TextTertiary"),
        ("Brush.Hover", "Color.Hover", "Color.Light.Hover"),
        ("Brush.Pressed", "Color.Pressed", "Color.Light.Pressed"),
    ];

    private static IslandBackground? _applied;

    /// <summary>
    /// Fill of the island body. Brushes stored in application resources are frozen by WPF and cannot be
    /// animated, so the body has its own brush that cross-fades; the theme brushes are swapped.
    /// </summary>
    public static SolidColorBrush BodyBrush { get; } = new(Colors.Black);

    /// <summary>Applies <paramref name="background"/>; the body colour animates unless it is the first call.</summary>
    public static void Apply(IslandBackground background)
    {
        if (_applied == background || Application.Current is not { } app)
        {
            return;
        }

        var animate = _applied is not null;
        _applied = background;
        foreach (var (brushKey, darkKey, lightKey) in Map)
        {
            var colorKey = background switch
            {
                IslandBackground.LightGray => lightKey,
                IslandBackground.Translucent when brushKey == "Island.Background" => "Color.IslandTranslucent",
                _ => darkKey,
            };
            if (app.TryFindResource(colorKey) is not Color target)
            {
                continue;
            }

            // DynamicResource users (text, surfaces, buttons) pick up the new brush at once.
            app.Resources[brushKey] = new SolidColorBrush(target);
            if (brushKey == "Island.Background")
            {
                if (animate)
                {
                    BodyBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                        new ColorAnimation(target, Duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
                }
                else
                {
                    BodyBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    BodyBrush.Color = target;
                }
            }
        }

        UI.Theming.ThemeEvents.RaiseChanged();
    }
}
