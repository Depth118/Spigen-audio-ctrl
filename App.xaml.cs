using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Device;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL
{
    public partial class App : Application
    {
        private SingleInstance? _instance;
        private AppSettings _settings = null!;
        private HeadsetController _headset = null!;
        private MainWindow _window = null!;
        private TrayIcon? _tray;
        private bool _exiting;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instance = new SingleInstance();
            if (!_instance.IsFirst)
            {
                _instance.ActivateFirstInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Error("App", "Fatal unhandled exception", args.ExceptionObject as Exception);

            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            Log.Info("App", $"Starting version {version} on {Environment.OSVersion}");

            _settings = AppSettings.Load();
            StartupRegistration.Refresh();
            var presets = new PresetLibrary(_settings);
            _headset = new HeadsetController();
            _headset.Changed += OnHeadsetChanged;

            _window = new MainWindow(_headset, presets, _settings);
            _window.Closing += OnWindowClosing;

            var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
            var icon = new System.Drawing.Icon(iconStream, System.Windows.Forms.SystemInformation.SmallIconSize);
            _tray = new TrayIcon(_headset, presets, icon, ShowWindow, ExitApp);
            _instance.OnActivationRequested(() => Dispatcher.BeginInvoke(ShowWindow));

            if (!e.Args.Contains(StartupRegistration.MinimizedArgument)) ShowWindow();
            _ = _headset.ConnectAsync(_settings.LastDeviceAddress);
        }

        private void OnHeadsetChanged(HeadsetChange change)
        {
            if (!change.HasFlag(HeadsetChange.Connection) || !_headset.IsConnected) return;
            if (_headset.DeviceAddress is ulong address && address != _settings.LastDeviceAddress)
            {
                _settings.LastDeviceAddress = address;
                _settings.Save();
            }
        }

        private void ShowWindow()
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (_exiting) return;
            if (!_settings.CloseToTray)
            {
                ExitApp();
                return;
            }

            e.Cancel = true;
            _window.Hide();
            if (!_settings.TrayHintShown)
            {
                _tray?.ShowHint("Still running", "Spigen Audio CTRL is in the notification area. Right-click the icon to exit.");
                _settings.TrayHintShown = true;
                _settings.Save();
            }
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            Log.Info("App", "Exiting");
            _headset.Disconnect();
            _settings.Save();
            _tray?.Dispose();
            _window.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _instance?.Dispose();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception.InnerException ?? e.Exception;
            Log.Error("App", "Unhandled UI exception", ex);
            MessageBox.Show($"Something went wrong: {ex.Message}\n\nDetails are in the log (Settings > Copy log).",
                "Spigen Audio CTRL", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
