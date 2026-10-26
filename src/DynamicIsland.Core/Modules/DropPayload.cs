namespace DynamicIsland.Core.Modules;

/// <summary>What was dropped on (or pasted into) the island, without any WPF types.</summary>
/// <param name="FilePaths">Files and folders (Explorer, desktop, most apps).</param>
/// <param name="Text">Plain text, or <c>null</c>.</param>
/// <param name="ImagePng">An image encoded as PNG (browser images, screenshots), or <c>null</c>.</param>
/// <param name="VirtualFiles">Files that exist only in the source app (browser downloads, mail attachments).</param>
public sealed record DropPayload(
    IReadOnlyList<string> FilePaths,
    string? Text,
    byte[]? ImagePng,
    IReadOnlyList<VirtualFile> VirtualFiles)
{
    /// <summary>Whether there is anything to take.</summary>
    public bool IsEmpty => FilePaths.Count == 0 && string.IsNullOrEmpty(Text) && ImagePng is null && VirtualFiles.Count == 0;
}

/// <summary>A file provided as content rather than a path.</summary>
public sealed record VirtualFile(string Name, byte[] Content);

/// <summary>A module that accepts drops (the Shelf).</summary>
public interface IDropHandler
{
    /// <summary>Takes the dropped content.</summary>
    Task HandleDropAsync(DropPayload payload, CancellationToken cancellationToken);
}

/// <summary>Services the island window offers to modules.</summary>
public interface IIslandHost
{
    /// <summary>Lets the island take keyboard focus (text input, Ctrl+V) until <see cref="ReleaseKeyboard"/>.</summary>
    void RequestKeyboard();

    /// <summary>Returns the island to its non-activating state.</summary>
    void ReleaseKeyboard();

    /// <summary>Opens the settings window, optionally at a module's section.</summary>
    void OpenSettings(string? moduleId = null);
}
