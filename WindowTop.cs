using System.Runtime.InteropServices;

namespace RegionClip;

static class WindowTop
{
    public const int Style = 0x00000008;

    public static void Keep(Form form)
    {
        if (form.IsDisposed || !form.IsHandleCreated)
            return;

        SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
