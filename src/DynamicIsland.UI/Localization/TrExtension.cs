using System.Windows.Data;
using System.Windows.Markup;

namespace DynamicIsland.UI.Localization;

/// <summary>
/// XAML shortcut for a live-updating localized string: <c>Text="{loc:Tr Tray.Exit}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    /// <summary>Creates the extension for <paramref name="key"/>.</summary>
    public TrExtension(string key)
    {
        Key = key;
    }

    /// <summary>Resource key.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
