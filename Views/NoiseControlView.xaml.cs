using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SpigenAudioCTRL.Device;

namespace SpigenAudioCTRL.Views
{
    public partial class NoiseControlView : UserControl
    {
        private HeadsetController _headset = null!;
        private Dictionary<AncMode, RadioButton> _tiles = new();
        private RadioButton[] _levels = Array.Empty<RadioButton>();
        private bool _rendering = true;

        public NoiseControlView()
        {
            InitializeComponent();
        }

        public void Initialize(HeadsetController headset)
        {
            _headset = headset;
            _tiles = new Dictionary<AncMode, RadioButton>
            {
                [AncModes.Adaptive] = ancAdaptive,
                [AncModes.Deep] = ancDeep,
                [AncModes.Commuting] = ancCommuting,
                [AncModes.Indoor] = ancIndoor,
                [AncModes.AntiWind] = ancAntiWind,
                [AncModes.Transparency] = ancTransparency,
                [AncModes.Off] = ancOff,
            };
            _levels = new[] { level0, level1, level2 };

            _headset.Changed += change =>
            {
                if ((change & (HeadsetChange.Anc | HeadsetChange.Gaming | HeadsetChange.Connection)) != 0) Render();
            };
            Render();
        }

        // Shows the headset's confirmed state; user input is ignored while rendering.
        private void Render()
        {
            _rendering = true;
            try
            {
                var current = _headset.Anc is AncSetting setting ? AncModes.Find(setting) : null;
                foreach (var (mode, tile) in _tiles) tile.IsChecked = mode == current;
                if (current?.HasLevels == true)
                {
                    _levels[Math.Min((int)_headset.Anc!.Value.Level, _levels.Length - 1)].IsChecked = true;
                }
                levelPanel.IsEnabled = current?.HasLevels == true;
                chkGamingMode.IsChecked = _headset.GamingMode == true;

                bool connected = _headset.IsConnected;
                IsEnabled = connected;
                Opacity = connected ? 1.0 : 0.55;
            }
            finally
            {
                _rendering = false;
            }
        }

        private AncMode? CheckedMode() => _tiles.FirstOrDefault(t => t.Value.IsChecked == true).Key;

        private byte SelectedLevel()
        {
            int index = Array.FindIndex(_levels, l => l.IsChecked == true);
            return index >= 0 ? (byte)index : AncModes.DefaultLevel;
        }

        private async void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (_rendering) return;
            var mode = _tiles.FirstOrDefault(t => t.Value == sender).Key;
            if (mode == null) return;

            levelPanel.IsEnabled = mode.HasLevels;
            await _headset.SetAncAsync(mode.ToSetting(SelectedLevel()));
        }

        private async void Level_Checked(object sender, RoutedEventArgs e)
        {
            if (_rendering) return;
            var mode = CheckedMode();
            if (mode?.HasLevels == true) await _headset.SetAncAsync(mode.ToSetting(SelectedLevel()));
        }

        private async void GamingMode_Click(object sender, RoutedEventArgs e)
        {
            await _headset.SetGamingModeAsync(chkGamingMode.IsChecked == true);
        }
    }
}
