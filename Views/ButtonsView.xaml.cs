using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SpigenAudioCTRL.Device;

namespace SpigenAudioCTRL.Views
{
    public partial class ButtonsView : UserControl
    {
        // Gestures and functions offered by Spigen's app for this headset.
        private static readonly (string Badge, string KeyName, string Gesture, byte Key, byte Action)[] Rows =
        {
            ("MFB", "Multi-Function Button", "Single Click", 7, 1),
            ("MFB", "Multi-Function Button", "Double Click", 7, 2),
            ("MFB", "Multi-Function Button", "Triple Click", 7, 3),
            ("MFB", "Multi-Function Button", "Four Clicks", 7, 4),
            ("+", "Volume Up Button", "Single Click", 5, 1),
            ("+", "Volume Up Button", "Long Press", 5, 5),
            ("-", "Volume Down Button", "Single Click", 3, 1),
            ("-", "Volume Down Button", "Long Press", 3, 5),
        };

        private static readonly (byte Code, string Name)[] Functions =
        {
            (0, "Disabled"),
            (1, "Play / Pause"),
            (2, "Previous Track"),
            (3, "Next Track"),
            (4, "Voice Assistant"),
            (5, "Volume Up"),
            (6, "Volume Down"),
            (7, "Gaming Mode"),
        };

        private HeadsetController _headset = null!;
        private readonly Dictionary<(byte Key, byte Action), ComboBox> _combos = new();
        private bool _rendering;

        public ButtonsView()
        {
            InitializeComponent();
        }

        public void Initialize(HeadsetController headset)
        {
            _headset = headset;
            BuildRows();
            _headset.Changed += change =>
            {
                if ((change & (HeadsetChange.Keys | HeadsetChange.Connection)) != 0) Render();
            };
            Render();
        }

        private void BuildRows()
        {
            rowsPanel.Children.Clear();
            foreach (var row in Rows)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

                var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = row.Badge,
                        Foreground = (Brush)FindResource("NordicInkPrimary"),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold
                    }
                });
                name.Children.Add(new TextBlock
                {
                    Text = row.KeyName,
                    Foreground = (Brush)FindResource("NordicInkPrimary"),
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center
                });
                grid.Children.Add(name);

                var gesture = new TextBlock
                {
                    Text = row.Gesture,
                    Foreground = (Brush)FindResource("NordicInkSecondary"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(gesture, 1);
                grid.Children.Add(gesture);

                var combo = new ComboBox
                {
                    Style = (Style)FindResource("NordicComboBoxStyle"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = false
                };
                AutomationProperties.SetName(combo, $"{row.KeyName}, {row.Gesture}");
                foreach (var (code, label) in Functions) combo.Items.Add(new ComboBoxItem { Content = label, Tag = code });

                var slot = (row.Key, row.Action);
                combo.SelectionChanged += async (_, _) =>
                {
                    if (_rendering || combo.SelectedItem is not ComboBoxItem { Tag: byte code }) return;
                    if (_headset.GetKeyFunction(slot) == code) return;
                    await _headset.SetKeyAsync(slot, code);
                };
                _combos[slot] = combo;
                Grid.SetColumn(combo, 2);
                grid.Children.Add(combo);

                rowsPanel.Children.Add(new Border
                {
                    Background = (Brush)FindResource("NordicSurfaceBrush"),
                    BorderBrush = (Brush)FindResource("NordicBorderBrush"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8, 12, 8),
                    Child = grid
                });
            }
        }

        private static void Select(ComboBox combo, byte code)
        {
            var item = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is byte tag && tag == code);
            if (item == null)
            {
                item = new ComboBoxItem { Content = $"Other (code {code})", Tag = code };
                combo.Items.Add(item);
            }
            combo.SelectedItem = item;
        }

        // Shows the headset's confirmed mappings; user input is ignored while rendering.
        private void Render()
        {
            _rendering = true;
            try
            {
                bool usable = _headset.IsConnected && _headset.KeysKnown;
                foreach (var (slot, combo) in _combos)
                {
                    var code = _headset.GetKeyFunction(slot);
                    if (!_headset.IsKeySupported(slot) || code == null)
                    {
                        combo.SelectedIndex = -1;
                        combo.IsEnabled = false;
                        combo.ToolTip = _headset.KeysKnown ? "Not supported by this headset" : null;
                        continue;
                    }
                    Select(combo, code.Value);
                    combo.IsEnabled = usable;
                    combo.ToolTip = null;
                }

                txtStatus.Text = !_headset.IsConnected
                    ? "Connect the headset to read its button settings."
                    : _headset.KeysKnown
                        ? "Current settings read from the headset. Changes are stored on the headset."
                        : "Reading the headset's button settings...";
            }
            finally
            {
                _rendering = false;
            }
        }
    }
}
