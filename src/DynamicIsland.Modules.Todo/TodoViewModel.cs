using System.Collections.ObjectModel;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Modules;
using DynamicIsland.Core.Storage;
using DynamicIsland.Modules.Todo.Services;
using DynamicIsland.UI.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicIsland.Modules.Todo;

/// <summary>One task row.</summary>
public sealed partial class TodoItemViewModel(TodoItem item) : ObservableObject
{
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _draft = item.Title;

    public TodoItem Item { get; } = item;

    public Guid Id => Item.Id;

    public string Title => Item.Title;

    public bool Done => Item.Done;
}

/// <summary>Todo page: add (Enter), edit, check off, delete, drag to reorder.</summary>
public sealed partial class TodoViewModel : ObservableObject
{
    /// <summary>File name relative to the data folder.</summary>
    public const string FileName = "todo.json";

    private readonly IStorageService _storage;
    private readonly TimeProvider _time;
    private readonly IIslandHost _host;
    private TodoList _list;

    [ObservableProperty]
    private string _newTitle = string.Empty;

    [ObservableProperty]
    private bool _isAdding;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    public TodoViewModel(IStorageService storage, TimeProvider time, IIslandHost host)
    {
        _storage = storage;
        _time = time;
        _host = host;
        _list = new TodoList(new TodoState(), time);
        Loc.Instance.CultureChanged += (_, _) => UpdateSummary();
    }

    /// <summary>Rows in display order.</summary>
    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    /// <summary>Loads saved tasks.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var state = await _storage.LoadAsync<TodoState>(FileName, cancellationToken) ?? new TodoState();
        state.Items ??= [];
        _list = new TodoList(state, _time);
        Reload();
    }

    /// <summary>Saves the list.</summary>
    public Task SaveAsync() => _storage.SaveAsync(FileName, _list.State);

    /// <summary>Moves a task (drag-to-reorder) and saves.</summary>
    public Task MoveAsync(TodoItemViewModel item, int newIndex)
    {
        _list.Move(item.Id, newIndex);
        return ChangedAsync();
    }

    /// <summary>Called by the view when a text box gets or loses focus.</summary>
    public void SetTyping(bool typing)
    {
        if (typing)
        {
            _host.RequestKeyboard();
        }
        else
        {
            _host.ReleaseKeyboard();
        }
    }

    [RelayCommand]
    private void BeginAdd()
    {
        IsAdding = true;
        _host.RequestKeyboard();
    }

    [RelayCommand]
    private async Task CommitAddAsync()
    {
        if (_list.Add(NewTitle) is not null)
        {
            NewTitle = string.Empty;
            await ChangedAsync();
        }
    }

    [RelayCommand]
    private void CancelAdd()
    {
        IsAdding = false;
        NewTitle = string.Empty;
        _host.ReleaseKeyboard();
    }

    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
        if (!IsEditMode)
        {
            foreach (var item in Items)
            {
                item.IsEditing = false;
            }
        }
    }

    [RelayCommand]
    private Task ToggleAsync(TodoItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        _list.Toggle(item.Id);
        return ChangedAsync();
    }

    [RelayCommand]
    private Task DeleteAsync(TodoItemViewModel? item)
    {
        if (item is null)
        {
            return Task.CompletedTask;
        }

        _list.Remove(item.Id);
        return ChangedAsync();
    }

    [RelayCommand]
    private void BeginRename(TodoItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.Draft = item.Title;
        item.IsEditing = true;
        _host.RequestKeyboard();
    }

    [RelayCommand]
    private async Task CommitRenameAsync(TodoItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsEditing = false;
        if (_list.Rename(item.Id, item.Draft))
        {
            await ChangedAsync();
        }
    }

    private async Task ChangedAsync()
    {
        Reload();
        await SaveAsync();
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var item in _list.Items)
        {
            Items.Add(new TodoItemViewModel(item));
        }

        IsEmpty = Items.Count == 0;
        UpdateSummary();
    }

    private void UpdateSummary() =>
        Summary = Loc.Instance.Format("Todo.Summary", _list.DoneToday, _list.Items.Count);
}

/// <summary>Todo list module.</summary>
public sealed class TodoModule(TodoViewModel viewModel) : IIslandModule
{
    private static readonly ResourceManager Strings = new("DynamicIsland.Modules.Todo.Resources.Strings", typeof(TodoModule).Assembly);

    public string Id => "todo";

    public string TitleKey => "Todo.Title";

    public string IconKey => "ListTodo";

    public int Order => 4;

    public object ExpandedViewModel => viewModel;

    public object? SettingsViewModel => null;

    public ILiveActivitySource? LiveActivities => null;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        Loc.Instance.Register(Strings);
        return viewModel.LoadAsync(cancellationToken);
    }

    public Task FlushAsync() => viewModel.SaveAsync();

    public Task ShutdownAsync() => viewModel.SaveAsync();
}

/// <summary>DI registration of the Todo module.</summary>
public static class TodoRegistration
{
    /// <summary>Registers the module and its templates.</summary>
    public static IServiceCollection AddTodoModule(this IServiceCollection services)
    {
        services.AddSingleton<TodoViewModel>();
        services.AddSingleton<TodoModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<TodoModule>());
        services.AddSingleton(new ModuleResources(new Uri("pack://application:,,,/DynamicIsland.Modules.Todo;component/Views/TodoTemplates.xaml")));
        return services;
    }
}
