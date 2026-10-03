using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Device;

namespace SpigenAudioCTRL.Infrastructure
{
    // Notification-area icon with quick noise control, EQ and gaming mode switches.
    public sealed class TrayIcon : IDisposable
    {
        private readonly HeadsetController _headset;
        private readonly PresetLibrary _presets;
        private readonly Action _showWindow;
        private readonly Action _exit;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu = new();

        public TrayIcon(HeadsetController headset, PresetLibrary presets, Icon icon, Action showWindow, Action exit)
        {
            _headset = headset;
            _presets = presets;
            _showWindow = showWindow;
            _exit = exit;

            _menu.Opening += (_, _) => BuildMenu();
            _icon = new NotifyIcon
            {
                Icon = icon,
                Text = "Spigen Audio CTRL",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) _showWindow();
            };
            _headset.Changed += _ => UpdateTooltip();
            BuildMenu();
        }

        public void ShowHint(string title, string text) =>
            _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

        private void UpdateTooltip()
        {
            string text = _headset.IsConnected
                ? _headset.BatteryLevel is int level ? $"Spigen Audio CTRL - battery {level}%" : "Spigen Audio CTRL - connected"
                : "Spigen Audio CTRL - not connected";
            _icon.Text = text;
        }

        private void BuildMenu()
        {
            _menu.Items.Clear();
            bool connected = _headset.IsConnected;

            string status = !connected ? "Not connected"
                : _headset.BatteryLevel is int battery ? $"Connected - battery {battery}%{(_headset.IsCharging ? " (charging)" : "")}"
                : "Connected";
            _menu.Items.Add(new ToolStripMenuItem($"SA-HP P10: {status}") { Enabled = false });
            _menu.Items.Add(new ToolStripSeparator());

            var anc = new ToolStripMenuItem("Noise control") { Enabled = connected };
            var currentMode = _headset.Anc is AncSetting setting ? AncModes.Find(setting) : null;
            byte ancLevel = currentMode?.HasLevels == true ? _headset.Anc!.Value.Level : AncModes.DefaultLevel;
            foreach (var mode in AncModes.All)
            {
                var item = new ToolStripMenuItem(mode.Name) { Checked = mode == currentMode };
                item.Click += async (_, _) => await _headset.SetAncAsync(mode.ToSetting(ancLevel));
                anc.DropDownItems.Add(item);
            }
            _menu.Items.Add(anc);

            var eq = new ToolStripMenuItem("Equalizer") { Enabled = connected };
            var currentPreset = _headset.Eq != null ? _presets.Match(_headset.Eq) : null;
            foreach (var preset in _presets.All)
            {
                var item = new ToolStripMenuItem(preset.Name) { Checked = preset == currentPreset };
                item.Click += async (_, _) => await _headset.SetEqAsync(preset.Bands);
                eq.DropDownItems.Add(item);
            }
            _menu.Items.Add(eq);

            var gaming = new ToolStripMenuItem("Gaming mode") { Enabled = connected, Checked = _headset.GamingMode == true };
            gaming.Click += async (_, _) => await _headset.SetGamingModeAsync(_headset.GamingMode != true);
            _menu.Items.Add(gaming);

            _menu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("Open Spigen Audio CTRL") { Font = new Font(_menu.Font, FontStyle.Bold) };
            open.Click += (_, _) => _showWindow();
            _menu.Items.Add(open);
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (_, _) => _exit();
            _menu.Items.Add(exit);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
