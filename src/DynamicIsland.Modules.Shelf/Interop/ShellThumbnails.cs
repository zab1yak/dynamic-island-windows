using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Modules.Shelf.Interop;

/// <summary>
/// Thumbnails and type icons from the Windows Shell (<c>IShellItemImageFactory</c>), the same images
/// Explorer shows. Requests run one at a time on a dedicated STA thread; results are frozen so the
/// UI thread can use them. Alpha is preserved by reading the DIB bits directly.
/// </summary>
public sealed class ShellThumbnails : IDisposable
{
    private const int SIIGBF_RESIZETOFIT = 0x0;
    private const int SIIGBF_BIGGERSIZEOK = 0x1;

    private readonly BlockingCollection<(string Path, int Size, TaskCompletionSource<ImageSource?> Result)> _queue = new();
    private readonly Thread _thread;

    public ShellThumbnails()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Shell thumbnails" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Gets a thumbnail (or the type icon) of <paramref name="path"/> at <paramref name="size"/> pixels.</summary>
    public Task<ImageSource?> GetAsync(string path, int size)
    {
        var result = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.TryAdd((path, size, result)))
        {
            result.SetResult(null);
        }

        return result.Task;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(1));
        _queue.Dispose();
    }

    private void Run()
    {
        foreach (var (path, size, result) in _queue.GetConsumingEnumerable())
        {
            try
            {
                result.SetResult(Load(path, size));
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or FileNotFoundException or UnauthorizedAccessException)
            {
                result.SetResult(null);
            }
        }
    }

    private static BitmapSource? Load(string path, int size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
        try
        {
            if (factory.GetImage(new SIZE(size, size), SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK, out var hbitmap) != 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return FromHBitmap(hbitmap);
            }
            finally
            {
                _ = DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(factory);
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0 || bm.bmWidth > 1024 || bm.bmHeight > 1024)
        {
            return null;
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = bm.bmWidth,
            biHeight = -bm.bmHeight, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var stride = bm.bmWidth * 4;
        var pixels = new byte[stride * bm.bmHeight];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)bm.bmHeight, pixels, ref header, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, dc);
        }

        // Shell bitmaps are premultiplied BGRA; some have no alpha at all (all zero) — then make them opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length && !hasAlpha; i += 4)
        {
            hasAlpha = pixels[i] != 0;
        }

        if (!hasAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }

        var source = BitmapSource.Create(bm.bmWidth, bm.bmHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SIZE(int cx, int cy)
    {
        public readonly int Cx = cx;
        public readonly int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

#pragma warning disable SYSLIB1054 // ComImport interfaces need the built-in marshaller.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, out BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
#pragma warning restore SYSLIB1054
}
