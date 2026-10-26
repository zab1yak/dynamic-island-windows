using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Modules;

namespace DynamicIsland.UI.Input;

/// <summary>
/// Converts a WPF drag/clipboard <see cref="IDataObject"/> into a <see cref="DropPayload"/>:
/// file drops, browser/mail virtual files (<c>FileGroupDescriptorW</c> + <c>FileContents</c>),
/// images (PNG or bitmap) and plain text.
/// </summary>
public static class DropPayloadReader
{
    /// <summary>Largest virtual file or image accepted from a drag (bytes).</summary>
    public const int MaxContentBytes = 200 * 1024 * 1024;

    /// <summary>Whether the data contains something the shelf can take.</summary>
    public static bool HasSupportedData(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("FileGroupDescriptorW")
        || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap)
        || data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    /// <summary>Reads everything usable from <paramref name="data"/>.</summary>
    public static DropPayload Read(IDataObject data)
    {
        var files = SafeGet<string[]>(data, DataFormats.FileDrop) ?? [];
        var virtualFiles = files.Length == 0 ? ReadVirtualFiles(data) : [];
        var png = files.Length == 0 && virtualFiles.Count == 0 ? ReadImage(data) : null;
        var text = SafeGet<string>(data, DataFormats.UnicodeText) ?? SafeGet<string>(data, DataFormats.Text);
        return new DropPayload(files, text, png, virtualFiles);
    }

    /// <summary>Reads the clipboard (call on the UI thread).</summary>
    public static DropPayload ReadClipboard()
    {
        try
        {
            return Clipboard.GetDataObject() is { } data ? Read(data) : new DropPayload([], null, null, []);
        }
        catch (COMException)
        {
            // Clipboard busy (another app holds it open).
            return new DropPayload([], null, null, []);
        }
    }

    private static List<VirtualFile> ReadVirtualFiles(IDataObject data)
    {
        var result = new List<VirtualFile>();
        if (SafeGet<MemoryStream>(data, "FileGroupDescriptorW") is not { } descriptor)
        {
            return result;
        }

        // FILEGROUPDESCRIPTORW: UINT count, then FILEDESCRIPTORW[count] (592 bytes each; name at offset 72, 260 WCHARs).
        var bytes = descriptor.ToArray();
        if (bytes.Length < 4)
        {
            return result;
        }

        var count = BitConverter.ToInt32(bytes, 0);
        const int DescriptorSize = 592;
        const int NameOffset = 72;
        // WPF's IDataObject only exposes the first FileContents stream (index 0).
        if (count >= 1 && bytes.Length >= 4 + DescriptorSize)
        {
            var name = Encoding.Unicode.GetString(bytes, 4 + NameOffset, 520).Split('\0')[0];
            if (SafeGet<MemoryStream>(data, "FileContents") is { Length: > 0 and <= MaxContentBytes } content)
            {
                result.Add(new VirtualFile(name, content.ToArray()));
            }
        }

        return result;
    }

    private static byte[]? ReadImage(IDataObject data)
    {
        if (SafeGet<MemoryStream>(data, "PNG") is { Length: > 0 and <= MaxContentBytes } png)
        {
            return png.ToArray();
        }

        if (SafeGet<BitmapSource>(data, DataFormats.Bitmap) is { } bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        return null;
    }

    private static T? SafeGet<T>(IDataObject data, string format)
        where T : class
    {
        try
        {
            return data.GetDataPresent(format) ? data.GetData(format) as T : null;
        }
        catch (Exception ex) when (ex is COMException or OutOfMemoryException or InvalidOperationException or ExternalException)
        {
            return null;
        }
    }
}
