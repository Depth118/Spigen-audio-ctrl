using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpigenAudioCTRL.Device;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL.Views
{
    public partial class SettingsView : UserControl
    {
        private const int CenterBalance = 50;

        private HeadsetController _headset = null!;
        private AppSettings _settings = null!;
        private readonly DispatcherTimer _balanceTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime? _timerSetAt;
        private bool _rendering = true;

        public SettingsView()
        {
            InitializeComponent();
            _balanceTimer.Tick += async (_, _) =>
            {
                _balanceTimer.Stop();
                await _headset.SetBalanceAsync((int)sliderBalance.Value);
            };
        }

        public void Initialize(HeadsetController headset, AppSettings settings)
        {
            _headset = headset;
            _settings = settings;

            chkStartup.IsChecked = StartupRegistration.IsEnabled;
            chkCloseToTray.IsChecked = settings.CloseToTray;
            string version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
            txtVersion.Text = $"Spigen Audio CTRL {version}. Unofficial; not affiliated with Spigen.";

            _headset.Changed += change =>
            {
                if ((change & (HeadsetChange.Balance | HeadsetChange.SleepTimer | HeadsetChange.Connection)) != 0) Render();
            };
            Render();
        }

        // Shows the headset's confirmed values; user input is ignored while rendering.
        private void Render()
        {
            _rendering = true;
            try
            {
                bool connected = _headset.IsConnected;
                headsetOptions.IsEnabled = connected;
                headsetOptions.Opacity = connected ? 1.0 : 0.55;

                if (!_balanceTimer.IsEnabled)
                {
                    sliderBalance.Value = _headset.Balance ?? CenterBalance;
                    UpdateBalanceLabel();
                }
                RenderSleepTimer();
            }
            finally
            {
                _rendering = false;
            }
        }

        private void UpdateBalanceLabel()
        {
            int offset = (int)sliderBalance.Value - CenterBalance;
            txtBalance.Text = offset == 0 ? "Centered" : offset < 0 ? $"Left {-offset}" : $"Right {offset}";
        }

        private void RenderSleepTimer()
        {
            var timer = _headset.SleepTimer;
            int minutes = timer is { IsActive: true } ? timer.Value.Minutes : SleepTimerState.Off;

            var options = ((Panel)timerOff.Parent).Children.OfType<RadioButton>().ToList();
            var match = options.FirstOrDefault(o => int.Parse((string)o.Tag) == minutes);
            foreach (var option in options) option.IsChecked = option == match;

            if (minutes == SleepTimerState.Off)
            {
                txtTimer.Text = "The headphones stay on until you turn them off.";
                _timerSetAt = null;
            }
            else
            {
                string when = _timerSetAt is DateTime setAt ? $" (around {setAt.AddMinutes(minutes):HH:mm})" : "";
                txtTimer.Text = match == null
                    ? $"A {minutes}-minute timer is running{when}."
                    : $"The headphones will turn off {minutes} minutes after the timer was set{when}.";
            }
        }

        private void Balance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_rendering) return;
            UpdateBalanceLabel();
            _balanceTimer.Stop();
            _balanceTimer.Start();
        }

        private void CenterBalance_Click(object sender, RoutedEventArgs e) => sliderBalance.Value = CenterBalance;

        private async void Timer_Checked(object sender, RoutedEventArgs e)
        {
            if (_rendering || sender is not RadioButton { Tag: string tag }) return;
            int minutes = int.Parse(tag);
            _timerSetAt = minutes == SleepTimerState.Off ? null : DateTime.Now;
            await _headset.SetSleepTimerAsync(minutes);
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(Window.GetWindow(this),
                "Restore the headphones' noise control, equalizer and button settings to factory defaults?",
                "Restore default settings", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) await _headset.RestoreDefaultsAsync();
        }

        private async void ClearPairing_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(Window.GetWindow(this),
                "Clear the headphones' pairing records?\n\nThey will forget every paired phone and PC, including this one. " +
                "You will need to pair them again in Windows Bluetooth settings.",
                "Clear pairing records", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) await _headset.ClearPairingRecordsAsync();
        }

        private void Startup_Click(object sender, RoutedEventArgs e)
        {
            StartupRegistration.SetEnabled(chkStartup.IsChecked == true);
            chkStartup.IsChecked = StartupRegistration.IsEnabled;
        }

        private void CloseToTray_Click(object sender, RoutedEventArgs e)
        {
            _settings.CloseToTray = chkCloseToTray.IsChecked == true;
            _settings.Save();
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            string log = Log.ReadAll();
            if (string.IsNullOrEmpty(log))
            {
                _headset.Notify("The log is empty.");
                return;
            }
            Clipboard.SetText(log);
            _headset.Notify("Log copied to the clipboard.");
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            System.IO.Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
        }
    }
}
