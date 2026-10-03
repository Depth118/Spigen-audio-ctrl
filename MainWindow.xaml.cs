using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Device;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL
{
    // Window shell: title bar, navigation, connection status and transient messages.
    public partial class MainWindow : Window
    {
        private readonly HeadsetController _headset;
        private readonly AppSettings _settings;
        private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

        public MainWindow(HeadsetController headset, PresetLibrary presets, AppSettings settings)
        {
            InitializeComponent();
            _headset = headset;
            _settings = settings;

            noiseView.Initialize(headset);
            eqView.Initialize(headset, presets, settings);
            buttonsView.Initialize(headset);
            settingsView.Initialize(headset, settings);

            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                txtToast.Visibility = Visibility.Collapsed;
            };
            _headset.Notice += ShowToast;
            _headset.Changed += change =>
            {
                if ((change & (HeadsetChange.Connection | HeadsetChange.Battery)) != 0) UpdateStatus();
            };

            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            StateChanged += (_, _) => maximizeIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized
                ? "M3,1.5 H10.5 V9 H3 Z M1.5,3.5 H9 V11 H1.5 Z"
                : "M1.5,1.5 H10.5 V10.5 H1.5 Z");
            UpdateStatus();
        }

        private void ShowToast(string message)
        {
            txtToast.Text = message;
            txtToast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void UpdateStatus()
        {
            switch (_headset.Connection)
            {
                case ConnectionState.Connected:
                    statusDot.Fill = (Brush)FindResource("NordicSuccessGreen");
                    txtStatus.Text = "Connected";
                    txtConnectAction.Text = "Disconnect";
                    break;
                case ConnectionState.Connecting:
                    statusDot.Fill = (Brush)FindResource("NordicGoldAccent");
                    txtStatus.Text = "Searching...";
                    txtConnectAction.Text = "Stop";
                    break;
                default:
                    statusDot.Fill = (Brush)FindResource("NordicMutedRed");
                    txtStatus.Text = "Not connected";
                    txtConnectAction.Text = "Connect";
                    break;
            }

            if (_headset.IsConnected && _headset.BatteryLevel is int level)
            {
                txtBattery.Text = _headset.IsCharging ? $"Battery: {level}% (charging)" : $"Battery: {level}%";
                txtBattery.Visibility = Visibility.Visible;
            }
            else
            {
                txtBattery.Visibility = Visibility.Collapsed;
            }
        }

        private async void ConnectAction_Click(object sender, RoutedEventArgs e)
        {
            if (_headset.Connection == ConnectionState.Disconnected)
            {
                await _headset.ConnectAsync(_settings.LastDeviceAddress);
            }
            else
            {
                _headset.Disconnect();
            }
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (noiseView == null) return;
            noiseView.Visibility = navAnc.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            eqView.Visibility = navEq.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            buttonsView.Visibility = navButtons.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            settingsView.Visibility = navSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
