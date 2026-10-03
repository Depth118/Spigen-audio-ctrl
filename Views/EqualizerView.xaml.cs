using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Device;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL.Views
{
    public partial class EqualizerView : UserControl
    {
        private const int BandCount = 10;
        private const double DbTop = 10.0;
        private const double DbBottom = -12.0;
        private const double PadX = 28;
        private const double PadY = 12;
        private const string HeadsetPresetName = "Headset EQ (current)";

        private HeadsetController _headset = null!;
        private PresetLibrary _presets = null!;
        private AppSettings _settings = null!;

        private readonly EqBand[] _bands = EqPresets.Stock.Bands.Select(b => b.Clone()).ToArray();
        private readonly Slider[] _sliders = new Slider[BandCount];
        private readonly TextBlock[] _gainLabels = new TextBlock[BandCount];
        private readonly TextBlock[] _freqLabels = new TextBlock[BandCount];
        private readonly Border[] _bandBorders = new Border[BandCount];
        private readonly List<EqPreset> _items = new();
        private readonly DispatcherTimer _sendTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private EqPreset? _headsetPreset;
        private int _version;
        private bool _sendInFlight;
        private bool _syncing = true;
        private int _selected = 3;
        private int _dragged = -1;

        public EqualizerView()
        {
            InitializeComponent();
            _sendTimer.Tick += (_, _) =>
            {
                _sendTimer.Stop();
                _ = SendAsync();
            };
        }

        public void Initialize(HeadsetController headset, PresetLibrary presets, AppSettings settings)
        {
            _headset = headset;
            _presets = presets;
            _settings = settings;

            chkStockOverlay.IsChecked = settings.ShowStockOverlay;
            BuildSliders();
            RebuildPresetList(null);
            LoadBands(_bands.Select(b => b.Clone()).ToArray());
            _syncing = false;

            _presets.Changed += () => RebuildPresetList(CurrentPreset());
            _headset.Changed += change =>
            {
                if (change.HasFlag(HeadsetChange.Eq)) OnHeadsetEq();
                if (change.HasFlag(HeadsetChange.Connection)) UpdateEnabled();
            };
            UpdateEnabled();
        }

        // Runs an update of controls without treating it as user input.
        private void Sync(Action update)
        {
            bool previous = _syncing;
            _syncing = true;
            try { update(); }
            finally { _syncing = previous; }
        }

        private void UpdateEnabled()
        {
            bool connected = _headset.IsConnected;
            IsEnabled = connected;
            Opacity = connected ? 1.0 : 0.55;
            if (!connected && _headset.Eq == null) txtDescription.Text = "Connect the headset to read its equalizer.";
        }

        #region Presets

        private EqPreset? CurrentPreset()
        {
            int index = cmbPresets.SelectedIndex;
            return index >= 0 && index < _items.Count ? _items[index] : null;
        }

        private void RebuildPresetList(EqPreset? select)
        {
            _items.Clear();
            _items.AddRange(_presets.All);
            if (_headsetPreset != null) _items.Add(_headsetPreset);

            // Keep the selection by name: saved presets are rebuilt as new objects.
            var selected = select == null ? null : _items.FirstOrDefault(p => p.Name == select.Name);
            Sync(() =>
            {
                cmbPresets.Items.Clear();
                foreach (var p in _items) cmbPresets.Items.Add(p.Name);
                cmbPresets.SelectedIndex = selected != null ? _items.IndexOf(selected) : -1;
            });
            UpdatePresetChrome(selected);
        }

        private void UpdatePresetChrome(EqPreset? preset)
        {
            btnDelete.Visibility = preset?.IsCustom == true ? Visibility.Visible : Visibility.Collapsed;
            if (preset != null) txtDescription.Text = preset.Description;
        }

        private void OnHeadsetEq()
        {
            // Local edits that haven't been confirmed yet take precedence.
            if (_sendTimer.IsEnabled || _sendInFlight || _headset.Eq == null) return;
            ShowHeadsetEq(_headset.Eq);
        }

        private void ShowHeadsetEq(IReadOnlyList<EqBand> bands)
        {
            LoadBands(bands);

            var match = _presets.Match(bands);
            if (match != null)
            {
                _headsetPreset = null;
                RebuildPresetList(match);
                return;
            }

            string description = "The curve currently stored on the headset.";
            double lowMidExcess = EqMath.ResponseDb(bands, 220) - EqMath.ResponseDb(EqPresets.Stock.Bands, 220);
            if (lowMidExcess > 4)
            {
                description += $" It leaves {lowMidExcess:0} dB more 220 Hz than Spigen's tuning, which tends to sound muddy; try \"{EqPresets.StockName}\".";
            }
            _headsetPreset = new EqPreset(HeadsetPresetName, description, bands.Select(b => b.Clone()).ToArray());
            RebuildPresetList(_headsetPreset);
        }

        private void Presets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            var preset = CurrentPreset();
            if (preset == null) return;

            UpdatePresetChrome(preset);
            LoadBands(preset.Bands);
            if (preset != _headsetPreset) SendNow();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var saved = _presets.Save(_bands);
            RebuildPresetList(saved);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var preset = CurrentPreset();
            if (preset == null || !preset.IsCustom) return;
            _presets.Delete(preset);
            txtDescription.Text = $"Deleted \"{preset.Name}\". The headset keeps the current curve.";
        }

        private void Stock_Click(object sender, RoutedEventArgs e)
        {
            RebuildPresetList(EqPresets.Stock);
            LoadBands(EqPresets.Stock.Bands);
            SendNow();
        }

        private void Personal_Click(object sender, RoutedEventArgs e)
        {
            var window = new PersonalEqWindow(_headset, _presets) { Owner = Window.GetWindow(this) };
            window.ShowDialog();
        }

        private void StockOverlay_Click(object sender, RoutedEventArgs e)
        {
            _settings.ShowStockOverlay = chkStockOverlay.IsChecked == true;
            _settings.Save();
            Draw();
        }

        #endregion

        #region Bands

        private void BuildSliders()
        {
            slidersGrid.Children.Clear();
            for (int i = 0; i < BandCount; i++)
            {
                int index = i;
                var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var border = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(2, 4, 2, 4),
                    Cursor = Cursors.Hand,
                    Child = column
                };
                border.MouseDown += (_, _) => SelectBand(index);
                _bandBorders[i] = border;

                _gainLabels[i] = new TextBlock
                {
                    Foreground = (Brush)FindResource("NordicInkPrimary"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var slider = new Slider
                {
                    Style = (Style)FindResource("NordicVerticalSliderStyle"),
                    Height = 85,
                    Minimum = EqMath.MinGain,
                    Maximum = EqMath.MaxGain
                };
                slider.GotKeyboardFocus += (_, _) => SelectBand(index);
                slider.ValueChanged += (_, _) =>
                {
                    if (_syncing) return;
                    _bands[index].Gain = RoundGain(slider.Value);
                    _gainLabels[index].Text = FormatGain(_bands[index].Gain);
                    if (_selected == index) UpdateInspector();
                    OnBandsEdited();
                };
                _sliders[i] = slider;

                _freqLabels[i] = new TextBlock
                {
                    Foreground = (Brush)FindResource("NordicInkTertiary"),
                    FontSize = 9.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                column.Children.Add(_gainLabels[i]);
                column.Children.Add(slider);
                column.Children.Add(_freqLabels[i]);
                slidersGrid.Children.Add(border);
            }
        }

        private static float RoundGain(double value) =>
            Math.Clamp((float)(Math.Round(value * 2) / 2.0), EqMath.MinGain, EqMath.MaxGain);

        private static string FormatGain(float gain) => $"{(gain > 0 ? "+" : "")}{gain:0.0}dB";

        private static string FormatFrequency(int f) => f >= 1000 ? $"{f / 1000.0:0.#}k" : $"{f}";

        private static string Classify(int f) => f switch
        {
            < 100 => "Sub-Bass",
            < 250 => "Bass",
            < 600 => "Low-Mid",
            < 2000 => "Mid",
            < 6000 => "Presence",
            < 12000 => "Treble",
            _ => "Air"
        };

        // Shows bands without sending them.
        private void LoadBands(IReadOnlyList<EqBand> bands)
        {
            Sync(() =>
            {
                for (int i = 0; i < BandCount; i++)
                {
                    _bands[i] = EqMath.Clamp(bands[i]);
                    _sliders[i].Value = _bands[i].Gain;
                    _freqLabels[i].Text = FormatFrequency(_bands[i].Frequency);
                }
                RefreshBandLabels();
            });
            SelectBand(_selected);
            UpdateHeadroom();
        }

        private void RefreshBandLabels()
        {
            for (int i = 0; i < BandCount; i++)
            {
                _gainLabels[i].Text = FormatGain(_bands[i].Gain);
                AutomationProperties.SetName(_sliders[i], $"{_bands[i].Frequency} hertz gain");
            }
        }

        private void SelectBand(int index)
        {
            if (index < 0 || index >= BandCount) return;
            _selected = index;
            for (int i = 0; i < BandCount; i++)
            {
                bool selected = i == index;
                _bandBorders[i].Background = selected ? new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) : Brushes.Transparent;
                _bandBorders[i].BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : Brushes.Transparent;
            }
            UpdateInspector();
            Draw();
        }

        private void UpdateInspector()
        {
            var band = _bands[_selected];
            Sync(() =>
            {
                txtBandName.Text = $"Band {_selected + 1} ({Classify(band.Frequency)})";
                sliderFreq.Value = EqMath.LogPosition(band.Frequency);
                txtFreq.Text = $"{band.Frequency:N0} Hz";
                sliderGain.Value = band.Gain;
                txtGain.Text = FormatGain(band.Gain).Replace("dB", " dB");
                sliderQ.Value = band.Q;
                txtQ.Text = $"Q: {band.Q:0.00}";
            });
        }

        private void UpdateHeadroom()
        {
            float headroom = EqMath.HeadroomGain(_bands);
            txtHeadroom.Text = $"Headroom: {headroom:0.0} dB";
            headroomDot.Fill = (Brush)FindResource(headroom < 0 ? "NordicGoldAccent" : "NordicSuccessGreen");
        }

        private void OnBandsEdited()
        {
            _version++;
            UpdateHeadroom();
            Draw();

            var match = _items.FirstOrDefault(p => p.Matches(_bands));
            Sync(() => cmbPresets.SelectedIndex = match != null ? _items.IndexOf(match) : -1);
            UpdatePresetChrome(match);
            if (match == null) txtDescription.Text = "Custom curve (not saved). Use Save to keep it.";

            _sendTimer.Stop();
            _sendTimer.Start();
        }

        private void SendNow()
        {
            _version++;
            _sendTimer.Stop();
            _ = SendAsync();
        }

        private async Task SendAsync()
        {
            int version = _version;
            _sendInFlight = true;
            bool ok;
            try
            {
                ok = await _headset.SetEqAsync(_bands.Select(b => b.Clone()).ToArray());
            }
            finally
            {
                _sendInFlight = false;
            }

            // On failure show what the headset really has, unless the user has edited since.
            if (!ok && version == _version && _headset.Eq != null) ShowHeadsetEq(_headset.Eq);
        }

        private void Freq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            SetFrequency(_selected, (int)Math.Round(EqMath.LogFrequency(sliderFreq.Value)));
        }

        private void SetFrequency(int index, int frequency)
        {
            _bands[index].Frequency = Math.Clamp(frequency, EqMath.MinFrequency, EqMath.MaxFrequency);
            _freqLabels[index].Text = FormatFrequency(_bands[index].Frequency);
            RefreshBandLabels();
            UpdateInspector();
            OnBandsEdited();
        }

        private void Gain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            SetGain(_selected, RoundGain(sliderGain.Value));
        }

        private void SetGain(int index, float gain)
        {
            _bands[index].Gain = Math.Clamp(gain, EqMath.MinGain, EqMath.MaxGain);
            Sync(() => _sliders[index].Value = _bands[index].Gain);
            _gainLabels[index].Text = FormatGain(_bands[index].Gain);
            UpdateInspector();
            OnBandsEdited();
        }

        private void Q_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            SetQ(_selected, (float)Math.Round(sliderQ.Value * 10) / 10.0f);
        }

        private void SetQ(int index, float q)
        {
            _bands[index].Q = (float)Math.Round(Math.Clamp(q, EqMath.MinQ, EqMath.MaxQ) * 10) / 10.0f;
            UpdateInspector();
            OnBandsEdited();
        }

        #endregion

        #region Canvas

        private double X(double frequency, double width) => PadX + EqMath.LogPosition(frequency) * (width - PadX * 2);

        private double Y(double db, double height)
        {
            double norm = (DbTop - db) / (DbTop - DbBottom);
            return Math.Clamp(PadY + norm * (height - PadY * 2), PadY, height - PadY);
        }

        private double DbAt(double y, double height) =>
            DbTop - Math.Clamp((y - PadY) / (height - PadY * 2), 0.0, 1.0) * (DbTop - DbBottom);

        private double FrequencyAt(double x, double width) =>
            Math.Round(EqMath.LogFrequency((x - PadX) / (width - PadX * 2)));

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

        private void Draw()
        {
            if (eqCanvas.ActualWidth < 10) return;

            eqCanvas.Children.Clear();
            double w = eqCanvas.ActualWidth;
            double h = eqCanvas.ActualHeight;
            var font = (FontFamily)FindResource("SegoeFluentFont");

            foreach (var db in new[] { 8, 4, 0, -4, -8 })
            {
                double y = Y(db, h);
                eqCanvas.Children.Add(new Line
                {
                    X1 = 24, Y1 = y, X2 = w - 24, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(db == 0 ? (byte)34 : (byte)15, 255, 255, 255)),
                    StrokeThickness = 1
                });
                var label = new TextBlock
                {
                    Text = $"{(db > 0 ? "+" : "")}{db}dB",
                    Foreground = new SolidColorBrush(Color.FromArgb(db == 0 ? (byte)140 : (byte)70, 255, 255, 255)),
                    FontSize = 8.5,
                    FontFamily = font
                };
                Canvas.SetLeft(label, 4);
                Canvas.SetTop(label, y - 6);
                eqCanvas.Children.Add(label);
            }

            foreach (var f in new[] { 50, 100, 250, 500, 1000, 2500, 5000, 10000, 20000 })
            {
                double x = X(f, w);
                eqCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = 8, X2 = x, Y2 = h - 12,
                    Stroke = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                    StrokeThickness = 1
                });
                var label = new TextBlock
                {
                    Text = FormatFrequency(f),
                    Foreground = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                    FontSize = 8,
                    FontFamily = font
                };
                Canvas.SetLeft(label, x - 6);
                Canvas.SetTop(label, h - 12);
                eqCanvas.Children.Add(label);
            }

            if (chkStockOverlay.IsChecked == true)
            {
                var stock = ResponsePath(EqPresets.Stock.Bands, w, h, closed: false);
                stock.Stroke = (Brush)FindResource("NordicGoldAccent");
                stock.StrokeThickness = 1.6;
                stock.StrokeDashArray = new DoubleCollection { 4, 3 };
                eqCanvas.Children.Add(stock);
            }

            var area = ResponsePath(_bands, w, h, closed: true);
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(24, 255, 255, 255), 0.0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
            area.Fill = fill;
            eqCanvas.Children.Add(area);

            var curve = ResponsePath(_bands, w, h, closed: false);
            curve.Stroke = Brushes.White;
            curve.StrokeThickness = 2.0;
            eqCanvas.Children.Add(curve);

            for (int i = 0; i < BandCount; i++)
            {
                double x = X(_bands[i].Frequency, w);
                double y = Y(_bands[i].Gain, h);
                if (i == _selected)
                {
                    var ring = new Ellipse
                    {
                        Width = 18, Height = 18,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.2,
                        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                    };
                    Canvas.SetLeft(ring, x - 9);
                    Canvas.SetTop(ring, y - 9);
                    eqCanvas.Children.Add(ring);
                }

                var outer = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White };
                Canvas.SetLeft(outer, x - 5);
                Canvas.SetTop(outer, y - 5);
                eqCanvas.Children.Add(outer);

                var inner = new Ellipse { Width = 4, Height = 4, Fill = (Brush)FindResource("NordicCanvasBrush") };
                Canvas.SetLeft(inner, x - 2);
                Canvas.SetTop(inner, y - 2);
                eqCanvas.Children.Add(inner);
            }
        }

        private Path ResponsePath(IReadOnlyList<EqBand> bands, double w, double h, bool closed)
        {
            const int samples = 160;
            var figure = new PathFigure();
            for (int s = 0; s <= samples; s++)
            {
                double f = EqMath.LogFrequency(s / (double)samples);
                var point = new Point(X(f, w), Y(EqMath.ResponseDb(bands, f), h));
                if (s == 0) figure.StartPoint = point;
                else figure.Segments.Add(new LineSegment(point, true));
            }
            if (closed)
            {
                figure.Segments.Add(new LineSegment(new Point(X(EqMath.MaxFrequency, w), h), true));
                figure.Segments.Add(new LineSegment(new Point(X(EqMath.MinFrequency, w), h), true));
                figure.IsClosed = true;
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return new Path { Data = geometry };
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            eqCanvas.Focus();
            var pos = e.GetPosition(eqCanvas);
            int closest = -1;
            double best = 35;
            for (int i = 0; i < BandCount; i++)
            {
                double dx = pos.X - X(_bands[i].Frequency, eqCanvas.ActualWidth);
                double dy = pos.Y - Y(_bands[i].Gain, eqCanvas.ActualHeight);
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < best)
                {
                    best = distance;
                    closest = i;
                }
            }

            if (closest != -1)
            {
                _dragged = closest;
                SelectBand(closest);
                eqCanvas.CaptureMouse();
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragged == -1) return;
            var pos = e.GetPosition(eqCanvas);
            var band = _bands[_dragged];
            band.Gain = RoundGain(DbAt(pos.Y, eqCanvas.ActualHeight));
            band.Frequency = (int)FrequencyAt(pos.X, eqCanvas.ActualWidth);

            Sync(() => _sliders[_dragged].Value = band.Gain);
            _gainLabels[_dragged].Text = FormatGain(band.Gain);
            _freqLabels[_dragged].Text = FormatFrequency(band.Frequency);
            UpdateInspector();
            OnBandsEdited();
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragged == -1) return;
            _dragged = -1;
            eqCanvas.ReleaseMouseCapture();
            SendNow();
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e) =>
            SetQ(_selected, _bands[_selected].Q + (e.Delta > 0 ? 0.1f : -0.1f));

        private void Canvas_KeyDown(object sender, KeyEventArgs e)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            switch (e.Key)
            {
                case Key.Left when shift:
                    SetFrequency(_selected, (int)Math.Round(_bands[_selected].Frequency / Math.Pow(2, 1.0 / 12)));
                    break;
                case Key.Right when shift:
                    SetFrequency(_selected, (int)Math.Round(_bands[_selected].Frequency * Math.Pow(2, 1.0 / 12)));
                    break;
                case Key.Left:
                    SelectBand(Math.Max(0, _selected - 1));
                    break;
                case Key.Right:
                    SelectBand(Math.Min(BandCount - 1, _selected + 1));
                    break;
                case Key.Up:
                    SetGain(_selected, _bands[_selected].Gain + 0.5f);
                    break;
                case Key.Down:
                    SetGain(_selected, _bands[_selected].Gain - 0.5f);
                    break;
                case Key.PageUp:
                    SetQ(_selected, _bands[_selected].Q + 0.1f);
                    break;
                case Key.PageDown:
                    SetQ(_selected, _bands[_selected].Q - 0.1f);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        #endregion
    }
}
