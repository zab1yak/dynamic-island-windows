using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Threading;
using DynamicIsland.Modules.Shelf.Interop;
using DynamicIsland.Modules.Shelf.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicIsland.Modules.Shelf;

/// <summary>One file on the shelf as shown in the list.</summary>
public sealed partial class ShelfItemViewModel(ShelfItem item, string path) : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    public ShelfItem Item { get; } = item;

    /// <summary>Absolute path of the file or folder.</summary>
    public string Path { get; } = path;

    public string Name => Item.Name;

    /// <summary>"1.2 MB · PNG" style caption.</summary>
    public string Details => Item.Kind == ShelfItemKind.Folder
        ? Loc.Instance["Shelf.Folder"]
        : $"{FormatSize(Item.SizeBytes)} · {System.IO.Path.GetExtension(Item.Name).TrimStart('.').ToUpperInvariant()}";

    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }
}

/// <summary>Shelf page: a temporary basket for files, images and text.</summary>
public sealed partial class ShelfViewModel : ObservableObject, IDropHandler, IDisposable
{
    private readonly ShelfStore _store;
    private readonly ShellThumbnails _thumbnails;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly IIslandHost _host;
    private readonly ILogger<ShelfViewModel> _logger;
    private ITimer? _cleanupTimer;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isBusy;

    public ShelfViewModel(ShelfStore store, ShellThumbnails thumbnails, IUiDispatcher dispatcher, TimeProvider time, IIslandHost host, ILogger<ShelfViewModel> logger)
    {
        _store = store;
        _thumbnails = thumbnails;
        _dispatcher = dispatcher;
        _time = time;
        _host = host;
        _logger = logger;
    }

    /// <summary>Items, newest first.</summary>
    public ObservableCollection<ShelfItemViewModel> Items { get; } = [];

    /// <summary>Shelf folder, for the settings window.</summary>
    public ShelfStore Store => _store;

    /// <summary>Loads the index, cleans up old items and starts the cleanup schedule.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _store.LoadAsync(cancellationToken);
        await _store.CleanupAsync(cancellationToken);
        Reload();
        // Clean up a few times a day; no other timers.
        _cleanupTimer = _time.CreateTimer(_ => _dispatcher.Post(() => _ = CleanupAsync()), null, TimeSpan.FromHours(6), TimeSpan.FromHours(6));
    }

    /// <inheritdoc />
    public async Task HandleDropAsync(DropPayload payload, CancellationToken cancellationToken)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var failed = false;
            if (payload.FilePaths.Count > 0)
            {
                var (_, error) = await _store.AddPathsAsync(payload.FilePaths, cancellationToken);
                if (error == ShelfAddError.TooLarge)
                {
                    ShowNotice(Loc.Instance.Format("Shelf.TooLarge", _store.Index.MaxSizeMb));
                }
                else
                {
                    failed |= error != ShelfAddError.None;
                }
            }

            foreach (var file in payload.VirtualFiles)
            {
                failed |= await _store.AddVirtualFileAsync(file.Name, file.Content, cancellationToken) is null;
            }

            if (payload.ImagePng is { } png)
            {
                failed |= await _store.AddImageAsync(png, cancellationToken) is null;
            }
            else if (payload.FilePaths.Count == 0 && payload.VirtualFiles.Count == 0 && !string.IsNullOrEmpty(payload.Text))
            {
                failed |= await _store.AddTextAsync(payload.Text, cancellationToken) is null;
            }

            if (failed)
            {
                ShowNotice(Loc.Instance["Shelf.SomeFailed"]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Adding to the shelf failed");
            ShowNotice(Loc.Instance["Shelf.SomeFailed"]);
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    /// <summary>Raised to ask the view to paste from the clipboard (the clipboard is a UI-thread WPF API).</summary>
    public event EventHandler? PasteRequested;

    /// <inheritdoc />
    public void Dispose() => _cleanupTimer?.Dispose();

    [RelayCommand]
    private void Paste() => PasteRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the item with its default app.</summary>
    [RelayCommand]
    private void Open(ShelfItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _logger.LogWarning(ex, "Opening a shelf item failed");
            ShowNotice(Loc.Instance["Shelf.OpenFailed"]);
        }
    }

    /// <summary>Selects the item in Explorer.</summary>
    [RelayCommand]
    private void Reveal(ShelfItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var _ = Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = false, ArgumentList = { "/select," + item.Path } });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Revealing a shelf item failed");
        }
    }

    /// <summary>Copies the paths of the selected items (or of <paramref name="item"/>).</summary>
    [RelayCommand]
    private void CopyPath(ShelfItemViewModel? item)
    {
        var paths = Selected(item).Select(i => i.Path).ToList();
        if (paths.Count > 0)
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }
    }

    /// <summary>Removes the selected items (or <paramref name="item"/>).</summary>
    [RelayCommand]
    private async Task RemoveAsync(ShelfItemViewModel? item)
    {
        await _store.RemoveAsync(Selected(item).Select(i => i.Item.Id).ToList());
        Reload();
    }

    /// <summary>Empties the shelf.</summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await _store.RemoveAsync(_store.Items.Select(i => i.Id).ToList());
        Reload();
    }

    /// <summary>Lets the island take the keyboard so Ctrl+V works on the shelf.</summary>
    [RelayCommand]
    private void Focus() => _host.RequestKeyboard();

    private List<ShelfItemViewModel> Selected(ShelfItemViewModel? clicked)
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (clicked is not null && !selected.Contains(clicked))
        {
            selected = [clicked];
        }

        return selected;
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (await _store.CleanupAsync() > 0)
            {
                Reload();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Shelf cleanup failed");
        }
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var item in _store.Items)
        {
            if (_store.ResolvePath(item) is { } path)
            {
                var vm = new ShelfItemViewModel(item, path);
                Items.Add(vm);
                _ = LoadThumbnailAsync(vm);
            }
        }

        IsEmpty = Items.Count == 0;
    }

    private async Task LoadThumbnailAsync(ShelfItemViewModel item)
    {
        var image = await _thumbnails.GetAsync(item.Path, 96);
        _dispatcher.Post(() => item.Thumbnail = image);
    }

    private void ShowNotice(string message)
    {
        Notice = message;
        _time.CreateTimer(_ => _dispatcher.Post(() =>
        {
            if (Notice == message)
            {
                Notice = null;
            }
        }), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
    }
}

/// <summary>Shelf module: accepts drops, keeps files until they are dragged out or cleaned up.</summary>
public sealed class ShelfModule(ShelfViewModel viewModel) : IIslandModule, IDropHandler
{
    /// <summary>Module id.</summary>
    public const string ModuleId = "shelf";

    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Shelf.Resources.Strings", typeof(ShelfModule).Assembly);

    public string Id => ModuleId;

    public string TitleKey => "Shelf.Title";

    public string IconKey => "Inbox";

    public int Order => 3;

    public object ExpandedViewModel => viewModel;

    public object? SettingsViewModel => null;

    public ILiveActivitySource? LiveActivities => null;

    public bool AcceptsDrop => true;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return viewModel.LoadAsync(cancellationToken);
    }

    public Task HandleDropAsync(DropPayload payload, CancellationToken cancellationToken) => viewModel.HandleDropAsync(payload, cancellationToken);

    public Task ShutdownAsync()
    {
        viewModel.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>DI registration of the Shelf module.</summary>
public static class ShelfRegistration
{
    /// <summary>Registers the module; files live in <c>%LOCALAPPDATA%\DynamicIsland\Shelf</c>.</summary>
    public static IServiceCollection AddShelfModule(this IServiceCollection services)
    {
        services.AddSingleton(sp => new ShelfStore(System.IO.Path.Combine(AppPaths.DataRoot, "Shelf"), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ShellThumbnails>();
        services.AddSingleton<ShelfViewModel>();
        services.AddSingleton<ShelfModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<ShelfModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Shelf;component/Views/ShelfTemplates.xaml")));
        return services;
    }
}
