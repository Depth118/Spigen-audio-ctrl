using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SpigenAudioCTRL.Infrastructure
{
    public static class DarkTitleBar
    {
        private const int UseImmersiveDarkMode = 20;
        private const int WindowCornerPreference = 33;
        private const int RoundCorners = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref enabled, sizeof(int));
            int corners = RoundCorners;
            DwmSetWindowAttribute(hwnd, WindowCornerPreference, ref corners, sizeof(int));
        }
    }
}
