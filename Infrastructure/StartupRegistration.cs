using System;
using Microsoft.Win32;

namespace SpigenAudioCTRL.Infrastructure
{
    // "Launch with Windows" via the current user's Run key; starts minimized to the tray.
    public static class StartupRegistration
    {
        public const string MinimizedArgument = "--minimized";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SpigenAudioCTRL";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled && Environment.ProcessPath is string exe)
                {
                    key.SetValue(ValueName, $"\"{exe}\" {MinimizedArgument}");
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Log.Warn("Startup", $"Couldn't update the Run key: {ex.Message}");
            }
        }

        // Keeps the registered path current if the executable was moved.
        public static void Refresh()
        {
            if (IsEnabled) SetEnabled(true);
        }
    }
}
