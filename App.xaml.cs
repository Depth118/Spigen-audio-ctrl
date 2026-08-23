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
            Debug.WriteLine($"[Unhandled Dispatcher Exception] {e.Exception}");
            MessageBox.Show($"Application Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Spigen Audio CTRL", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Debug.WriteLine($"[Unhandled Domain Exception] {ex}");
                MessageBox.Show($"Fatal Error: {ex.Message}", "Spigen Audio CTRL", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
