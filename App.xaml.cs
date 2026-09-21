using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace SpigenAudioCTRL
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception.InnerException ?? e.Exception;
            Debug.WriteLine($"[Unhandled Dispatcher Exception] {ex}");
            MessageBox.Show($"Application Error: {ex.Message}\n\n{ex.StackTrace}", "Spigen Audio CTRL", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception rawEx)
            {
                var ex = rawEx.InnerException ?? rawEx;
                Debug.WriteLine($"[Unhandled Domain Exception] {ex}");
                MessageBox.Show($"Fatal Error: {ex.Message}\n\n{ex.StackTrace}", "Spigen Audio CTRL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
