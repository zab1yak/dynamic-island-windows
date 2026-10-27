namespace DynamicIsland.UI.Theming;

/// <summary>
/// Raised (on the UI thread) after the theme brushes were swapped. Controls that look brushes up in
/// <c>OnRender</c> instead of through <c>DynamicResource</c> redraw on it.
/// </summary>
public static class ThemeEvents
{
    /// <summary>The theme brushes changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>Notifies listeners; called by the theme switcher.</summary>
    public static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);
}
