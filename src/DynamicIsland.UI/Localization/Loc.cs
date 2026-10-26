using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace DynamicIsland.UI.Localization;

/// <summary>
/// Runtime-switchable string lookup. Each assembly (UI, App, every module) registers its own
/// <see cref="ResourceManager"/>; XAML binds to the indexer through <see cref="TrExtension"/>, so
/// changing <see cref="Culture"/> updates every visible string without a restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private readonly List<ResourceManager> _managers = [Resources.Strings.ResourceManager];
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private Loc()
    {
    }

    /// <summary>The single instance used by bindings.</summary>
    public static Loc Instance { get; } = new();

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the culture changed.</summary>
    public event EventHandler? CultureChanged;

    /// <summary>Current UI culture.</summary>
    public CultureInfo Culture => _culture;

    /// <summary>Returns the localized string for <paramref name="key"/>, or the key itself when it is missing.</summary>
    public string this[string key]
    {
        get
        {
            foreach (var manager in _managers)
            {
                var value = manager.GetString(key, _culture);
                if (value is not null)
                {
                    return value;
                }
            }

            return key;
        }
    }

    /// <summary>Formats the localized string with <paramref name="args"/> in the current culture.</summary>
    public string Format(string key, params object?[] args) => string.Format(_culture, this[key], args);

    /// <summary>Adds a resource manager (call once per assembly at start-up).</summary>
    public void Register(ResourceManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (!_managers.Contains(manager))
        {
            _managers.Add(manager);
        }
    }

    /// <summary>
    /// Switches the language. <c>null</c> follows Windows; unsupported cultures fall back to English.
    /// </summary>
    public void SetLanguage(string? language)
    {
        var name = language ?? CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        var culture = name == "ru" ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.GetCultureInfo("en-US");
        if (Equals(culture, _culture))
        {
            return;
        }

        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
