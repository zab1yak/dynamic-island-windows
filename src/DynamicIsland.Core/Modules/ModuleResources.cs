namespace DynamicIsland.Core.Modules;

/// <summary>
/// A module's XAML resource dictionary (DataTemplates keyed by view-model type), merged into the
/// application resources on start-up.
/// </summary>
/// <param name="Dictionary">Pack URI, for example
/// <c>pack://application:,,,/DynamicIsland.Modules.Todo;component/Views/TodoTemplates.xaml</c>.</param>
public sealed record ModuleResources(Uri Dictionary);
