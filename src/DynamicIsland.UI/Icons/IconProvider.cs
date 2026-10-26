using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.UI.Icons;

/// <summary>
/// Single entry point for icons. Glyphs are 24×24 stroke geometries stored as
/// <c>Icon.&lt;Name&gt;</c> resources (generated from Lucide by <c>tools/import_lucide.py</c>);
/// replacing the icon set means regenerating that one dictionary.
/// </summary>
public static class IconProvider
{
    /// <summary>Size of the design grid every glyph is drawn on.</summary>
    public const double GridSize = 24;

    /// <summary>Returns the geometry for <paramref name="name"/> (for example <c>Play</c>), or <c>null</c>.</summary>
    public static Geometry? Get(string? name)
    {
        if (string.IsNullOrEmpty(name) || Application.Current is null)
        {
            return null;
        }

        return Application.Current.TryFindResource("Icon." + name) as Geometry;
    }
}
