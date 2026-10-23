using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RegionClip;

static class ScreenCapture
{
    const uint SrcCopy = 0x00CC0020;
    const uint MonitorDefaultToNearest = 2;

    public static Bitmap Capture(Rectangle region)
    {
        if (region.Width < 1 || region.Height < 1)
            throw new ArgumentException("A região selecionada é inválida.", nameof(region));

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var destination = graphics.GetHdc();
        var source = GetDC(IntPtr.Zero);
        try
        {
            if (!BitBlt(destination, 0, 0, region.Width, region.Height, source, region.X, region.Y, SrcCopy))
                throw new InvalidOperationException("Não foi possível capturar a tela.");
        }
        finally
        {
            graphics.ReleaseHdc(destination);
            ReleaseDC(IntPtr.Zero, source);
        }

        ApplyMonitorDpi(bitmap, region);
        return bitmap;
    }

    public static void CopyToClipboard(Image image)
    {
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, true, image);
        Clipboard.SetDataObject(data, copy: true, retryTimes: 8, retryDelay: 100);
    }

    static void ApplyMonitorDpi(Bitmap bitmap, Rectangle region)
    {
        var center = new NativePoint
        {
            X = region.X + (region.Width / 2),
            Y = region.Y + (region.Height / 2),
        };
        var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            bitmap.SetResolution(dpiX, dpiY);
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(
        IntPtr hdcDest,
        int xDest,
        int yDest,
        int width,
        int height,
        IntPtr hdcSrc,
        int xSrc,
        int ySrc,
        uint rop);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}
