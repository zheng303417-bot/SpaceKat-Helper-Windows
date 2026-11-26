using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SpaceKatMonitor
{
    public partial class MainWindow
    {
        private const int DwmBorderColor = 34, DwmCaptionColor = 35, DwmTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // Windows 11 lets the title bar take the window's own warm grey so the
        // frame reads as one ceramic surface. Older Windows keeps its frame.
        private void ApplyWindowChrome()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            try
            {
                int caption = 0x00E0E6E9, text = 0x001C1E1F, border = 0x00CBD4D9; // COLORREF 0x00BBGGRR
                DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
                DwmSetWindowAttribute(handle, DwmBorderColor, ref border, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
