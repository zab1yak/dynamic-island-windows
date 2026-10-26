namespace DynamicIsland.Core;

/// <summary>Well-known folders of the application.</summary>
public static class AppPaths
{
    /// <summary>Folder name under <c>%LOCALAPPDATA%</c>.</summary>
    public const string AppFolderName = "DynamicIsland";

    /// <summary><c>%LOCALAPPDATA%\DynamicIsland</c>: settings and module data.</summary>
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary><c>%LOCALAPPDATA%\DynamicIsland\logs</c>.</summary>
    public static string Logs { get; } = Path.Combine(DataRoot, "logs");
}
