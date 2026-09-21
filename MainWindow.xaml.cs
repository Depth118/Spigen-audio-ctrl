using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SpigenAudioCTRL
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        private readonly BluetoothService _bt = new();

        // 10-Band Parametric EQ State
        private readonly EqBand[] _bands = new EqBand[10];
        private readonly Slider[] _sliders = new Slider[10];
        private readonly TextBlock[] _gainLabels = new TextBlock[10];
        private readonly TextBlock[] _freqLabels = new TextBlock[10];
        private readonly Border[] _sliderBorders = new Border[10];
        private int _selectedBandIdx = 4; // Default to 1kHz
        private int _draggedNodeIdx = -1;
        private bool _isUpdatingInspector = true;

        // Harman 2018 Reference Gains for standard 10 frequencies
        private static readonly float[] HarmanReferenceGains = new float[] { 4.5f, 1.5f, 0.0f, 0.0f, 1.5f, 3.0f, 4.0f, 2.0f, 1.0f, 0.0f };

        // Debounce timer — sends EQ to device 150ms after the last slider move
        private readonly DispatcherTimer _eqDebounceTimer;

        public class EqPresetModel
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public EqBand[] Bands { get; set; }

            public EqPresetModel(string name, string description, EqBand[] bands)
            {
                Name = name;
                Description = description;
                Bands = bands;
            }
        }

        private readonly Dictionary<string, EqPresetModel> _presets = new()
        {
            {
                "P10 Audiophile Master (Recommended)",
                new EqPresetModel(
                    "P10 Audiophile Master (Recommended)",
                    "Acoustically calibrated for P10 hardware: sub-bass rumble (+3.5dB), surgical anti-mud cut (-2.5dB at 220Hz), vocal pinna restoration (+3.0dB at 2.5kHz), and anti-sibilance control.",
                    new EqBand[]
                    {
                        new(60, 3.5f, 0.50f),
                        new(220, -2.5f, 1.20f),
                        new(500, -1.5f, 1.40f),
                        new(1000, 0.0f, 1.40f),
                        new(2000, 1.5f, 1.30f),
                        new(2500, 3.0f, 1.20f),
                        new(5000, 2.0f, 1.30f),
                        new(7500, -1.5f, 1.80f),
                        new(12000, 3.0f, 1.20f),
                        new(16000, 2.0f, 1.00f)
                    })
            },
            {
                "P10 Dynamic V-Shape Rumble",
                new EqPresetModel(
                    "P10 Dynamic V-Shape Rumble",
                    "Deep 45Hz sub-bass punch with scooped low-mids and sparkling highs, tuned specifically for EDM, hip-hop, and modern pop.",
                    new EqBand[]
                    {
                        new(45, 5.5f, 0.45f),
                        new(150, 2.0f, 1.00f),
                        new(350, -2.0f, 1.40f),
                        new(1000, -0.5f, 1.40f),
                        new(2000, 1.0f, 1.20f),
                        new(2500, 2.5f, 1.20f),
                        new(5000, 2.5f, 1.30f),
                        new(7500, 0.0f, 1.40f),
                        new(12000, 3.5f, 1.20f),
                        new(16000, 2.5f, 1.00f)
                    })
            },
            {
                "Harman Over-Ear Target 2018",
                new EqPresetModel(
                    "Harman Over-Ear Target 2018",
                    "Benchmark audiophile curve preferred for natural tonality, deep sub-bass extension, and lifelike vocal rise.",
                    new EqBand[]
                    {
                        new(60, 4.5f, 0.45f),
                        new(220, 1.0f, 1.2f),
                        new(500, 0.0f, 1.4f),
                        new(1000, 0.0f, 1.4f),
                        new(2000, 1.5f, 1.4f),
                        new(3000, 3.5f, 1.2f),
                        new(5000, 2.5f, 1.4f),
                        new(7500, 1.2f, 1.4f),
                        new(12000, 0.5f, 1.2f),
                        new(16000, 0.0f, 1.0f)
                    })
            },
            {
                "Diffuse Field Studio Reference",
                new EqPresetModel(
                    "Diffuse Field Studio Reference",
                    "Uncolored, analytical studio monitor curve with flat pinna compensation for precision mixing and critical listening.",
                    new EqBand[]
                    {
                        new(60, 0.0f, 0.5f),
                        new(220, 0.0f, 1.2f),
                        new(500, 0.0f, 1.5f),
                        new(1000, 0.0f, 1.5f),
                        new(2000, 2.0f, 1.4f),
                        new(2500, 3.5f, 1.2f),
                        new(5000, 1.5f, 1.2f),
                        new(7500, -1.0f, 1.4f),
                        new(12000, 0.0f, 1.2f),
                        new(16000, 0.0f, 1.0f)
                    })
            },
            {
                "Vocal Clarity & Anti-Sibilance",
                new EqPresetModel(
                    "Vocal Clarity & Anti-Sibilance",
                    "Surgical -3.5dB notch at 7.5kHz eliminating piercing 's/t' treble fatigue while boosting vocal presence at 1.5k–3kHz.",
                    new EqBand[]
                    {
                        new(60, 1.0f, 0.5f),
                        new(220, 0.5f, 1.2f),
                        new(500, 0.0f, 1.5f),
                        new(1000, 1.5f, 1.5f),
                        new(2000, 3.0f, 1.4f),
                        new(2500, 3.5f, 1.2f),
                        new(5000, 0.5f, 1.2f),
                        new(7500, -3.5f, 2.2f),
                        new(12000, 0.0f, 1.2f),
                        new(16000, -1.0f, 1.0f)
                    })
            },
            {
                "Deep Sub-Bass Clean Rumble",
                new EqPresetModel(
                    "Deep Sub-Bass Clean Rumble",
                    "Heavy sub-bass lift at 45Hz without bleeding into vocals, clearing low-mid mud for pristine electronic & hip-hop.",
                    new EqBand[]
                    {
                        new(45, 6.0f, 0.45f),
                        new(120, 3.5f, 1.0f),
                        new(350, -1.5f, 1.5f),
                        new(1000, 0.0f, 1.4f),
                        new(2000, 0.5f, 1.4f),
                        new(2500, 1.5f, 1.2f),
                        new(5000, 2.0f, 1.2f),
                        new(7500, 1.0f, 1.2f),
                        new(12000, 2.0f, 1.2f),
                        new(16000, 3.0f, 1.0f)
                    })
            },
            {
                "Warm Audiophile Soundstage",
                new EqPresetModel(
                    "Warm Audiophile Soundstage",
                    "Smooth, analog vinyl warmth with rich midbody presence and gentle high-frequency rolloff for fatigue-free listening.",
                    new EqBand[]
                    {
                        new(60, 3.5f, 0.5f),
                        new(220, 2.5f, 1.1f),
                        new(500, 1.5f, 1.4f),
                        new(1000, 0.5f, 1.4f),
                        new(2000, 0.0f, 1.2f),
                        new(2500, 1.0f, 1.2f),
                        new(5000, 1.0f, 1.2f),
                        new(7500, -1.0f, 1.2f),
                        new(12000, -2.0f, 1.2f),
                        new(16000, -3.0f, 1.0f)
                    })
            },
            {
                "Gaming Spatial & Footstep",
                new EqPresetModel(
                    "Gaming Spatial & Footstep",
                    "Acoustic spatial profile emphasizing subtle footsteps, weapon reloads, and directional cues around 2kHz-5kHz.",
                    new EqBand[]
                    {
                        new(60, 1.5f, 0.6f),
                        new(220, -1.0f, 1.2f),
                        new(500, -2.0f, 1.5f),
                        new(1000, 1.5f, 1.4f),
                        new(2000, 3.5f, 1.4f),
                        new(2500, 4.5f, 1.2f),
                        new(5000, 4.0f, 1.2f),
                        new(7500, 3.0f, 1.4f),
                        new(12000, 2.0f, 1.2f),
                        new(16000, 2.5f, 1.0f)
                    })
            },
            {
                "Default Flat (Reference 0dB)",
                new EqPresetModel(
                    "Default Flat (Reference 0dB)",
                    "Pure neutral baseline across all 10 hardware DSP bands with 0dB gain.",
                    new EqBand[]
                    {
                        new(60, 0, 0.4f), new(220, 0, 1.2f), new(500, 0, 1.5f), new(1000, 0, 1.5f),
                        new(2000, 0, 1.2f), new(2500, 0, 0.8f), new(5000, 0, 1.2f), new(7500, 0, 1.2f),
                        new(12000, 0, 1.2f), new(16000, 0, 1.2f)
                    })
            }
        };

        public MainWindow()
        {
            // 1. Initialize default bands BEFORE InitializeComponent
            for (int i = 0; i < 10; i++)
            {
                int freq = (i < RcspProtocol.EqFrequencies.Length) ? RcspProtocol.EqFrequencies[i] : 1000;
                float q = (i < RcspProtocol.EqQValues.Length) ? RcspProtocol.EqQValues[i] : 1.2f;
                _bands[i] = new EqBand(freq, 0.0f, q);
            }

            // 2. Initialize EQ debounce timer BEFORE InitializeComponent
            _eqDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _eqDebounceTimer.Tick += (s, e) =>
            {
                _eqDebounceTimer.Stop();
                float masterGain = RcspProtocol.CalculateAntiClippingPreAmp(_bands);
                _ = _bt.SetEqualizerAsync(_bands, masterGain);
            };

            _isUpdatingInspector = true;
            InitializeComponent();

            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;

            _bt.OnConnectionStateChanged += Bt_OnConnectionStateChanged;
            _bt.OnHardwareSyncReceived += Bt_OnHardwareSyncReceived;
            _bt.OnBatteryLevelUpdated += Bt_OnBatteryLevelUpdated;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            int roundCorner = 2;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorner, sizeof(int));
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitEqualizerUI();
            InitButtonRemappingUI();
            _isUpdatingInspector = false;

            UpdateTitleBarStatus(false, "Connecting...");
            txtConnectAction.Text = "Connecting...";

            await _bt.ConnectAsync();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _eqDebounceTimer.Stop();
            _bt.Disconnect();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                maximizeIcon.Data = Geometry.Parse("M3,1.5 H10.5 V9 H3 Z M1.5,3.5 H9 V11 H1.5 Z");
            }
            else
            {
                maximizeIcon.Data = Geometry.Parse("M1.5,1.5 H10.5 V10.5 H1.5 Z");
            }
        }

        #region Title Bar & Navigation Handlers

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (panelAnc == null || panelEq == null || panelButtons == null) return;

            panelAnc.Visibility = (navAnc.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            panelEq.Visibility = (navEq.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            panelButtons.Visibility = (navButtons.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;

            if (navEq.IsChecked == true)
            {
                DrawEqualizerCurve();
            }
        }

        private void UpdateTitleBarStatus(bool connected, string name)
        {
            Dispatcher.Invoke(() =>
            {
                if (connected)
                {
                    tbStatusDot.Fill = (SolidColorBrush)FindResource("NordicSuccessGreen");
                    tbStatusText.Text = "Connected";
                    txtConnectAction.Text = "Disconnect";
                    txtDeviceSubtitle.Text = "Connected";
                }
                else
                {
                    tbStatusDot.Fill = (SolidColorBrush)FindResource("NordicMutedRed");
                    tbStatusText.Text = name == "Connecting..." ? "Connecting..." : "Disconnected";
                    txtConnectAction.Text = name == "Connecting..." ? "Connecting..." : "Pair Headphones";
                    txtDeviceSubtitle.Text = "No Device Attached";
                    // Fix #5 (audit): Hide battery when not connected
                    txtBatteryLevel.Visibility = Visibility.Collapsed;
                }
            });
        }

        // Fix #3 (audit): async void handlers now catch exceptions so they don't silently crash
        private async void BtnConnectAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bt.IsConnected)
                {
                    _bt.Disconnect();
                }
                else
                {
                    UpdateTitleBarStatus(false, "Connecting...");
                    await _bt.ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Connect] {ex.Message}");
            }
        }

        #endregion

        #region Noise Control Handlers

        // Fix #3 (audit): Wrapped in try/catch — exceptions in async void would otherwise be unhandled
        private async void AncMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_bt.IsConnected) return;
            try
            {
                int ancId = 66816; // Adaptive ANC default
                if (sender == ancDeep) ancId = 65794;
                else if (sender == ancAdaptive) ancId = 66816;
                else if (sender == ancCommuting) ancId = 66048;
                else if (sender == ancAntiWind) ancId = 66560;
                else if (sender == ancTransparency) ancId = 196868;
                else if (sender == ancOff) ancId = 131072;

                await _bt.SetAncModeAsync(ancId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ANC] {ex.Message}");
            }
        }

        // Fix #3 (audit): Wrapped in try/catch
        private async void ChkGamingMode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool enabled = (chkGamingMode.IsChecked == true);
                await _bt.SetGamingModeAsync(enabled);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GamingMode] {ex.Message}");
            }
        }

        #endregion

        #region 10-Band Parametric Equalizer Engine

        private void InitEqualizerUI()
        {
            // Populate Presets
            cmbEqPresets.Items.Clear();
            foreach (var p in _presets.Keys)
            {
                cmbEqPresets.Items.Add(p);
            }
            cmbEqPresets.SelectedIndex = 0;

            // Build 10 Vertical Slider Columns in Rack
            eqSlidersGrid.Children.Clear();
            for (int i = 0; i < 10; i++)
            {
                int index = i;
                var col = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var border = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(2, 4, 2, 4),
                    Cursor = Cursors.Hand,
                    Tag = index
                };

                // Clicking the slider column selects the band in the inspector
                border.MouseDown += (s, ev) => SelectBand(index);
                _sliderBorders[i] = border;

                var lblGain = new TextBlock
                {
                    Text = "+0.0dB",
                    Foreground = (SolidColorBrush)FindResource("NordicInkPrimary"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                _gainLabels[i] = lblGain;

                var slider = new Slider
                {
                    Style = (Style)FindResource("NordicVerticalSliderStyle"),
                    Height = 85,
                    Tag = index,
                    Value = _bands[i].Gain
                };

                slider.ValueChanged += (s, ev) =>
                {
                    if (_isUpdatingInspector) return;
                    _bands[index].Gain = (float)Math.Round(slider.Value * 2) / 2.0f;
                    _gainLabels[index].Text = $"{(_bands[index].Gain > 0 ? "+" : "")}{_bands[index].Gain:0.0}dB";
                    
                    if (_selectedBandIdx == index)
                    {
                        _isUpdatingInspector = true;
                        if (sliderInspectorGain != null) sliderInspectorGain.Value = _bands[index].Gain;
                        if (txtInspectorGain != null) txtInspectorGain.Text = $"{(_bands[index].Gain > 0 ? "+" : "")}{_bands[index].Gain:0.0} dB";
                        _isUpdatingInspector = false;
                    }

                    UpdateAntiClippingDisplay();
                    DrawEqualizerCurve();
                    _eqDebounceTimer?.Stop();
                    _eqDebounceTimer?.Start();
                };
                _sliders[i] = slider;

                int freq = _bands[i].Frequency;
                string freqText = (freq >= 1000) ? $"{freq / 1000.0:0.#}k" : $"{freq}";
                var lblFreq = new TextBlock
                {
                    Text = freqText,
                    Foreground = (SolidColorBrush)FindResource("NordicInkTertiary"),
                    FontSize = 9.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                _freqLabels[i] = lblFreq;

                col.Children.Add(lblGain);
                col.Children.Add(slider);
                col.Children.Add(lblFreq);

                border.Child = col;
                eqSlidersGrid.Children.Add(border);
            }

            // Apply default preset
            ApplyPreset("P10 Audiophile Master (Recommended)");
            SelectBand(4); // Focus 1 kHz
        }

        private void ApplyPreset(string name)
        {
            if (_presets.TryGetValue(name, out var preset))
            {
                if (txtPresetDescription != null) txtPresetDescription.Text = preset.Description;

                for (int i = 0; i < 10; i++)
                {
                    if (i < preset.Bands.Length)
                    {
                        _bands[i] = preset.Bands[i].Clone();
                    }

                    if (_sliders[i] != null) _sliders[i].Value = _bands[i].Gain;
                    if (_gainLabels[i] != null) _gainLabels[i].Text = $"{(_bands[i].Gain > 0 ? "+" : "")}{_bands[i].Gain:0.0}dB";
                    if (_freqLabels[i] != null)
                    {
                        int f = _bands[i].Frequency;
                        _freqLabels[i].Text = (f >= 1000) ? $"{f / 1000.0:0.#}k" : $"{f}";
                    }
                }

                SelectBand(_selectedBandIdx);
                UpdateAntiClippingDisplay();
                DrawEqualizerCurve();

                float masterGain = RcspProtocol.CalculateAntiClippingPreAmp(_bands);
                _ = _bt.SetEqualizerAsync(_bands, masterGain);
            }
        }

        private void SelectBand(int idx)
        {
            if (idx < 0 || idx >= 10 || _bands[idx] == null) return;
            _selectedBandIdx = idx;

            // Highlight selected slider rack column
            for (int i = 0; i < 10; i++)
            {
                if (_sliderBorders[i] != null)
                {
                    bool isSel = (i == idx);
                    _sliderBorders[i].Background = isSel ? new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) : Brushes.Transparent;
                    _sliderBorders[i].BorderBrush = isSel ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : Brushes.Transparent;
                }
            }

            int f = _bands[idx].Frequency;
            float g = _bands[idx].Gain;
            float q = _bands[idx].Q;

            if (txtInspectorBandName != null) txtInspectorBandName.Text = $"Band {idx + 1} ({GetBandClassification(f)})";
            if (txtInspectorBandType != null) txtInspectorBandType.Text = (idx == 0) ? "Low-Shelf / Sub Bass" : ((idx == 9) ? "High-Shelf / Brilliance" : "Parametric Peaking Bell");

            _isUpdatingInspector = true;
            if (sliderInspectorFreq != null) sliderInspectorFreq.Value = f;
            if (txtInspectorFreq != null) txtInspectorFreq.Text = $"{f:N0} Hz";

            if (sliderInspectorGain != null) sliderInspectorGain.Value = g;
            if (txtInspectorGain != null) txtInspectorGain.Text = $"{(g > 0 ? "+" : "")}{g:0.0} dB";

            if (sliderInspectorQ != null) sliderInspectorQ.Value = q;
            if (txtInspectorQ != null) txtInspectorQ.Text = $"Q: {q:0.00}";
            _isUpdatingInspector = false;

            DrawEqualizerCurve();
        }

        private static string GetBandClassification(int freq)
        {
            if (freq < 100) return "Sub-Bass";
            if (freq < 250) return "Bass";
            if (freq < 600) return "Low-Mid";
            if (freq < 2000) return "Mid";
            if (freq < 6000) return "Presence";
            if (freq < 12000) return "Treble";
            return "Air";
        }

        private void SliderInspectorFreq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInspector || _selectedBandIdx < 0 || _selectedBandIdx >= 10 || sliderInspectorFreq == null) return;

            int f = (int)sliderInspectorFreq.Value;
            _bands[_selectedBandIdx].Frequency = f;
            if (txtInspectorFreq != null) txtInspectorFreq.Text = $"{f:N0} Hz";
            if (txtInspectorBandName != null) txtInspectorBandName.Text = $"Band {_selectedBandIdx + 1} ({GetBandClassification(f)})";

            if (_freqLabels[_selectedBandIdx] != null)
            {
                _freqLabels[_selectedBandIdx].Text = (f >= 1000) ? $"{f / 1000.0:0.#}k" : $"{f}";
            }

            DrawEqualizerCurve();
            _eqDebounceTimer?.Stop();
            _eqDebounceTimer?.Start();
        }

        private void SliderInspectorGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInspector || _selectedBandIdx < 0 || _selectedBandIdx >= 10 || sliderInspectorGain == null) return;

            float g = (float)Math.Round(sliderInspectorGain.Value * 2) / 2.0f;
            _bands[_selectedBandIdx].Gain = g;
            if (txtInspectorGain != null) txtInspectorGain.Text = $"{(g > 0 ? "+" : "")}{g:0.0} dB";

            if (_sliders[_selectedBandIdx] != null)
            {
                _isUpdatingInspector = true;
                _sliders[_selectedBandIdx].Value = g;
                _isUpdatingInspector = false;
            }

            if (_gainLabels[_selectedBandIdx] != null)
            {
                _gainLabels[_selectedBandIdx].Text = $"{(g > 0 ? "+" : "")}{g:0.0}dB";
            }

            UpdateAntiClippingDisplay();
            DrawEqualizerCurve();
            _eqDebounceTimer?.Stop();
            _eqDebounceTimer?.Start();
        }

        private void SliderInspectorQ_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInspector || _selectedBandIdx < 0 || _selectedBandIdx >= 10 || sliderInspectorQ == null) return;

            float q = (float)Math.Round(sliderInspectorQ.Value * 10) / 10.0f;
            _bands[_selectedBandIdx].Q = q;
            if (txtInspectorQ != null) txtInspectorQ.Text = $"Q: {q:0.00}";

            DrawEqualizerCurve();
            _eqDebounceTimer?.Stop();
            _eqDebounceTimer?.Start();
        }

        private void EqCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_selectedBandIdx < 0 || _selectedBandIdx >= 10 || _bands[_selectedBandIdx] == null) return;

            float delta = (e.Delta > 0) ? 0.1f : -0.1f;
            float newQ = Math.Clamp(_bands[_selectedBandIdx].Q + delta, 0.2f, 5.0f);
            newQ = (float)Math.Round(newQ * 10) / 10.0f;

            _bands[_selectedBandIdx].Q = newQ;

            _isUpdatingInspector = true;
            if (sliderInspectorQ != null) sliderInspectorQ.Value = newQ;
            if (txtInspectorQ != null) txtInspectorQ.Text = $"Q: {newQ:0.00}";
            _isUpdatingInspector = false;

            DrawEqualizerCurve();
            _eqDebounceTimer?.Stop();
            _eqDebounceTimer?.Start();
        }

        private void UpdateAntiClippingDisplay()
        {
            if (txtPreAmpStatus == null || preAmpDot == null) return;
            float masterGain = RcspProtocol.CalculateAntiClippingPreAmp(_bands);
            if (masterGain < 0)
            {
                txtPreAmpStatus.Text = $"Pre-Amp: {masterGain:0.0} dB (Safe)";
                preAmpDot.Fill = (SolidColorBrush)FindResource("NordicGoldAccent");
            }
            else
            {
                txtPreAmpStatus.Text = "Pre-Amp: 0.0 dB";
                preAmpDot.Fill = (SolidColorBrush)FindResource("NordicSuccessGreen");
            }
        }

        private void CmbEqPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbEqPresets.SelectedItem is string name)
            {
                ApplyPreset(name);
            }
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            int n = 1;
            string customName;
            do { customName = $"Custom {n++}"; } while (_presets.ContainsKey(customName));

            var clonedBands = new EqBand[10];
            for (int i = 0; i < 10; i++) clonedBands[i] = _bands[i].Clone();

            _presets[customName] = new EqPresetModel(customName, "User-configured custom parametric curve.", clonedBands);
            cmbEqPresets.Items.Add(customName);
            cmbEqPresets.SelectedItem = customName;
        }

        private void BtnResetEq_Click(object sender, RoutedEventArgs e)
        {
            cmbEqPresets.SelectedItem = "Default Flat (Reference 0dB)";
        }

        private void ChkHarmanTarget_Click(object sender, RoutedEventArgs e)
        {
            DrawEqualizerCurve();
        }

        private void EqCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawEqualizerCurve();

        // Logarithmic frequency to canvas X mapping
        private double GetLogX(double freq, double width)
        {
            double padding = 28;
            double usableWidth = width - (padding * 2);
            double clampedFreq = Math.Clamp(freq, 20, 20000);
            double norm = (Math.Log10(clampedFreq) - Math.Log10(20)) / 3.0; // log10(20000)-log10(20) = 3.0
            return padding + (norm * usableWidth);
        }

        private double GetFreqFromX(double x, double width)
        {
            double padding = 28;
            double usableWidth = width - (padding * 2);
            double norm = Math.Clamp((x - padding) / usableWidth, 0.0, 1.0);
            double logFreq = Math.Log10(20) + (norm * 3.0);
            return Math.Round(Math.Pow(10, logFreq));
        }

        private double GetNodeY(double db, double height)
        {
            double padding = 16;
            double usableHeight = height - (padding * 2);
            double norm = (8.0 - db) / 16.0;
            return padding + (norm * usableHeight);
        }

        private double GetDbFromY(double y, double height)
        {
            double padding = 16;
            double usableHeight = height - (padding * 2);
            double clampedY = Math.Max(padding, Math.Min(height - padding, y));
            double norm = (clampedY - padding) / usableHeight;
            double db = 8.0 - (norm * 16.0);
            return Math.Round(db * 2) / 2.0;
        }

        // Mathematical composite peaking/bell filter transfer response
        private double CalculateCompositeGain(double f)
        {
            double totalGain = 0.0;
            for (int i = 0; i < 10; i++)
            {
                double f0 = _bands[i].Frequency;
                double g = _bands[i].Gain;
                double q = _bands[i].Q;

                if (f0 <= 0 || q <= 0 || Math.Abs(g) < 0.01) continue;

                double ratio = (f / f0) - (f0 / f);
                double response = g / (1.0 + (q * q * ratio * ratio));
                totalGain += response;
            }
            return Math.Clamp(totalGain, -8.0, 8.0);
        }

        private void DrawEqualizerCurve()
        {
            if (eqCanvas == null || eqCanvas.ActualWidth < 10) return;

            eqCanvas.Children.Clear();
            double w = eqCanvas.ActualWidth;
            double h = eqCanvas.ActualHeight;

            // 1. Draw dB Grid Horizontal Lines
            int[] dbLevels = new int[] { -8, -4, 0, 4, 8 };
            foreach (var db in dbLevels)
            {
                double y = GetNodeY(db, h);
                var line = new Line
                {
                    X1 = 24, Y1 = y,
                    X2 = w - 24, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(db == 0 ? (byte)30 : (byte)15, 255, 255, 255)),
                    StrokeThickness = 1
                };
                eqCanvas.Children.Add(line);

                var lbl = new TextBlock
                {
                    Text = $"{(db > 0 ? "+" : "")}{db}dB",
                    Foreground = new SolidColorBrush(Color.FromArgb(db == 0 ? (byte)140 : (byte)70, 255, 255, 255)),
                    FontSize = 8.5,
                    FontFamily = (FontFamily)FindResource("SegoeFluentFont")
                };
                Canvas.SetLeft(lbl, 4);
                Canvas.SetTop(lbl, y - 6);
                eqCanvas.Children.Add(lbl);
            }

            // 2. Draw Logarithmic Frequency Grid Vertical Lines
            int[] gridFreqs = new int[] { 50, 100, 250, 500, 1000, 2500, 5000, 10000, 20000 };
            foreach (var gf in gridFreqs)
            {
                double x = GetLogX(gf, w);
                var line = new Line
                {
                    X1 = x, Y1 = 12,
                    X2 = x, Y2 = h - 12,
                    Stroke = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                    StrokeThickness = 1
                };
                eqCanvas.Children.Add(line);

                string fText = (gf >= 1000) ? $"{gf / 1000}k" : $"{gf}";
                var lblF = new TextBlock
                {
                    Text = fText,
                    Foreground = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                    FontSize = 8,
                    FontFamily = (FontFamily)FindResource("SegoeFluentFont")
                };
                Canvas.SetLeft(lblF, x - 6);
                Canvas.SetTop(lblF, h - 12);
                eqCanvas.Children.Add(lblF);
            }

            // 3. Draw Harman Target Reference Ghost Curve (Gold Dashed)
            if (chkHarmanTarget.IsChecked == true)
            {
                var harmanPath = CreateHarmanReferencePath(w, h);
                harmanPath.Stroke = (SolidColorBrush)FindResource("NordicGoldAccent");
                harmanPath.StrokeThickness = 1.6;
                harmanPath.StrokeDashArray = new DoubleCollection { 4, 3 };
                eqCanvas.Children.Add(harmanPath);
            }

            // 4. Draw Active Composite Parametric Area Fill
            var areaPath = CreateParametricAreaPath(w, h);
            eqCanvas.Children.Add(areaPath);

            // 5. Draw Active Solid White Curve
            var activePath = CreateParametricCurvePath(w, h);
            activePath.Stroke = Brushes.White;
            activePath.StrokeThickness = 2.0;
            eqCanvas.Children.Add(activePath);

            // 6. Draw 10 Interactive Handle Nodes
            for (int i = 0; i < 10; i++)
            {
                double x = GetLogX(_bands[i].Frequency, w);
                double y = GetNodeY(_bands[i].Gain, h);
                bool isSelected = (i == _selectedBandIdx);

                if (isSelected)
                {
                    // Active selection glowing ring
                    var focusRing = new Ellipse
                    {
                        Width = 18, Height = 18,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.2,
                        Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                    };
                    Canvas.SetLeft(focusRing, x - 9);
                    Canvas.SetTop(focusRing, y - 9);
                    eqCanvas.Children.Add(focusRing);
                }

                var outer = new Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = Brushes.White
                };
                Canvas.SetLeft(outer, x - 5);
                Canvas.SetTop(outer, y - 5);
                eqCanvas.Children.Add(outer);

                var inner = new Ellipse
                {
                    Width = 4, Height = 4,
                    Fill = (SolidColorBrush)FindResource("NordicCanvasBrush")
                };
                Canvas.SetLeft(inner, x - 2);
                Canvas.SetTop(inner, y - 2);
                eqCanvas.Children.Add(inner);
            }
        }

        private Path CreateParametricCurvePath(double w, double h)
        {
            var path = new Path();
            var geometry = new PathGeometry();
            int sampleCount = 100;

            double fMin = 20;
            double fMax = 20000;
            double logMin = Math.Log10(fMin);
            double logMax = Math.Log10(fMax);

            double firstFreq = Math.Pow(10, logMin);
            double firstGain = CalculateCompositeGain(firstFreq);
            var figure = new PathFigure { StartPoint = new Point(GetLogX(firstFreq, w), GetNodeY(firstGain, h)) };

            for (int s = 1; s <= sampleCount; s++)
            {
                double norm = s / (double)sampleCount;
                double freq = Math.Pow(10, logMin + (norm * (logMax - logMin)));
                double gain = CalculateCompositeGain(freq);
                figure.Segments.Add(new LineSegment(new Point(GetLogX(freq, w), GetNodeY(gain, h)), true));
            }

            geometry.Figures.Add(figure);
            path.Data = geometry;
            return path;
        }

        private Path CreateParametricAreaPath(double w, double h)
        {
            var path = new Path();
            var geometry = new PathGeometry();
            int sampleCount = 100;

            double fMin = 20;
            double fMax = 20000;
            double logMin = Math.Log10(fMin);
            double logMax = Math.Log10(fMax);

            double firstFreq = Math.Pow(10, logMin);
            double firstGain = CalculateCompositeGain(firstFreq);
            var figure = new PathFigure { StartPoint = new Point(GetLogX(firstFreq, w), GetNodeY(firstGain, h)) };

            for (int s = 1; s <= sampleCount; s++)
            {
                double norm = s / (double)sampleCount;
                double freq = Math.Pow(10, logMin + (norm * (logMax - logMin)));
                double gain = CalculateCompositeGain(freq);
                figure.Segments.Add(new LineSegment(new Point(GetLogX(freq, w), GetNodeY(gain, h)), true));
            }

            figure.Segments.Add(new LineSegment(new Point(GetLogX(fMax, w), GetNodeY(-8, h)), true));
            figure.Segments.Add(new LineSegment(new Point(GetLogX(fMin, w), GetNodeY(-8, h)), true));
            figure.IsClosed = true;

            geometry.Figures.Add(figure);
            path.Data = geometry;

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(24, 255, 255, 255), 0.0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));

            path.Fill = gradient;
            return path;
        }

        private Path CreateHarmanReferencePath(double w, double h)
        {
            var path = new Path();
            var geometry = new PathGeometry();
            int sampleCount = 80;

            double fMin = 20;
            double fMax = 20000;
            double logMin = Math.Log10(fMin);
            double logMax = Math.Log10(fMax);

            double firstFreq = Math.Pow(10, logMin);
            double firstGain = EvaluateHarmanGain(firstFreq);
            var figure = new PathFigure { StartPoint = new Point(GetLogX(firstFreq, w), GetNodeY(firstGain, h)) };

            for (int s = 1; s <= sampleCount; s++)
            {
                double norm = s / (double)sampleCount;
                double freq = Math.Pow(10, logMin + (norm * (logMax - logMin)));
                double gain = EvaluateHarmanGain(freq);
                figure.Segments.Add(new LineSegment(new Point(GetLogX(freq, w), GetNodeY(gain, h)), true));
            }

            geometry.Figures.Add(figure);
            path.Data = geometry;
            return path;
        }

        private static double EvaluateHarmanGain(double f)
        {
            // Continuous Harman Over-Ear 2018 Target Acoustic Formula
            // 1. Sub-bass / Bass shelf (+4.8 dB below 50Hz, rolling off to 0 dB around 200Hz)
            double bass = 4.8 / (1.0 + Math.Pow(f / 105.0, 2.2));

            // 2. Ear canal / Pinna gain (+3.6 dB peaking at ~3000 Hz)
            double pinnaRatio = (f / 3000.0) - (3000.0 / f);
            double pinna = 3.6 / (1.0 + (1.1 * 1.1 * pinnaRatio * pinnaRatio));

            // 3. High treble presence / air shelf
            double trebleRatio = (f / 7500.0) - (7500.0 / f);
            double treble = 1.2 / (1.0 + (1.5 * 1.5 * trebleRatio * trebleRatio));

            return Math.Clamp(bass + pinna + treble, -8.0, 8.0);
        }

        private void EqCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(eqCanvas);
            double w = eqCanvas.ActualWidth;
            double h = eqCanvas.ActualHeight;

            // Find closest band node
            int closest = -1;
            double minDist = 35;
            for (int i = 0; i < 10; i++)
            {
                double nx = GetLogX(_bands[i].Frequency, w);
                double ny = GetNodeY(_bands[i].Gain, h);
                double dist = Math.Sqrt(Math.Pow(pos.X - nx, 2) + Math.Pow(pos.Y - ny, 2));
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = i;
                }
            }

            if (closest != -1)
            {
                _draggedNodeIdx = closest;
                SelectBand(closest);
                eqCanvas.CaptureMouse();
            }
        }

        private void EqCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedNodeIdx != -1)
            {
                var pos = e.GetPosition(eqCanvas);
                double w = eqCanvas.ActualWidth;
                double h = eqCanvas.ActualHeight;

                double db = GetDbFromY(pos.Y, h);
                double freq = GetFreqFromX(pos.X, w);

                _bands[_draggedNodeIdx].Gain = (float)db;
                _bands[_draggedNodeIdx].Frequency = (int)freq;

                SelectBand(_draggedNodeIdx);
                UpdateAntiClippingDisplay();
                DrawEqualizerCurve();

                _eqDebounceTimer.Stop();
                _eqDebounceTimer.Start();
            }
        }

        private void EqCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedNodeIdx != -1)
            {
                _draggedNodeIdx = -1;
                eqCanvas.ReleaseMouseCapture();

                float masterGain = RcspProtocol.CalculateAntiClippingPreAmp(_bands);
                _ = _bt.SetEqualizerAsync(_bands, masterGain);
            }
        }

        #endregion

        #region Button Remapping UI & Dispatch

        private class KeyRemapRow
        {
            public string KeyBadge { get; set; } = "";
            public string KeyName { get; set; } = "";
            public string GestureName { get; set; } = "";
            public int KeyNum { get; set; }
            public int ActionType { get; set; }
            public int DefaultFunc { get; set; }
        }

        private static readonly Dictionary<int, string> FunctionNames = new()
        {
            { 1, "Play / Pause" },
            { 3, "Next Track" },
            { 2, "Previous Track" },
            { 5, "Volume Up" },
            { 6, "Volume Down" },
            { 4, "Voice Assistant" },
            { 7, "Gaming Mode Toggle" },
            { 8, "ANC Mode Switch" },
            { 0, "Disabled" }
        };

        private void InitButtonRemappingUI()
        {
            buttonRemapList.Children.Clear();

            var rows = new List<KeyRemapRow>
            {
                new() { KeyBadge = "MFB", KeyName = "Multi-Function Button", GestureName = "Single Click", KeyNum = 7, ActionType = 1, DefaultFunc = 1 },
                new() { KeyBadge = "MFB", KeyName = "Multi-Function Button", GestureName = "Double Click", KeyNum = 7, ActionType = 2, DefaultFunc = 3 },
                new() { KeyBadge = "MFB", KeyName = "Multi-Function Button", GestureName = "Triple Click", KeyNum = 7, ActionType = 3, DefaultFunc = 2 },
                new() { KeyBadge = "+", KeyName = "Volume Up Button", GestureName = "Single Click", KeyNum = 5, ActionType = 1, DefaultFunc = 5 },
                new() { KeyBadge = "+", KeyName = "Volume Up Button", GestureName = "Long Press", KeyNum = 5, ActionType = 5, DefaultFunc = 3 },
                new() { KeyBadge = "-", KeyName = "Volume Down Button", GestureName = "Single Click", KeyNum = 3, ActionType = 1, DefaultFunc = 6 },
                new() { KeyBadge = "-", KeyName = "Volume Down Button", GestureName = "Long Press", KeyNum = 3, ActionType = 5, DefaultFunc = 2 }
            };

            foreach (var r in rows)
            {
                var border = new Border
                {
                    Background = (SolidColorBrush)FindResource("NordicSurfaceBrush"),
                    BorderBrush = (SolidColorBrush)FindResource("NordicBorderBrush"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

                // Physical Control Badge + Name
                var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = r.KeyBadge,
                    Foreground = (SolidColorBrush)FindResource("NordicInkPrimary"),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };
                leftPanel.Children.Add(badge);
                leftPanel.Children.Add(new TextBlock
                {
                    Text = r.KeyName,
                    Foreground = (SolidColorBrush)FindResource("NordicInkPrimary"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                });

                Grid.SetColumn(leftPanel, 0);
                grid.Children.Add(leftPanel);

                // Trigger Gesture
                var txtGesture = new TextBlock
                {
                    Text = r.GestureName,
                    Foreground = (SolidColorBrush)FindResource("NordicInkSecondary"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(txtGesture, 1);
                grid.Children.Add(txtGesture);

                // Assigned Action ComboBox
                var cmb = new ComboBox
                {
                    Style = (Style)FindResource("NordicComboBoxStyle"),
                    Tag = r,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                foreach (var kvp in FunctionNames)
                {
                    cmb.Items.Add(new ComboBoxItem { Content = kvp.Value, Tag = kvp.Key });
                }

                // Set default selected item
                for (int ci = 0; ci < cmb.Items.Count; ci++)
                {
                    if (cmb.Items[ci] is ComboBoxItem item && (int)item.Tag == r.DefaultFunc)
                    {
                        cmb.SelectedIndex = ci;
                        break;
                    }
                }

                cmb.SelectionChanged += async (s, ev) =>
                {
                    if (cmb.SelectedItem is ComboBoxItem selected && selected.Tag is int funcId)
                    {
                        try
                        {
                            await _bt.SetKeyMappingAsync(r.KeyNum, r.ActionType, funcId);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Remap] {ex.Message}");
                        }
                    }
                };

                Grid.SetColumn(cmb, 2);
                grid.Children.Add(cmb);

                border.Child = grid;
                buttonRemapList.Children.Add(border);
            }
        }

        #endregion

        #region Diagnostics & Hardware Log Handlers

        private void Bt_OnConnectionStateChanged(bool connected, string name)
        {
            UpdateTitleBarStatus(connected, name);
        }

        // Fix #5 (audit): Display battery level in the sidebar device card
        private void Bt_OnBatteryLevelUpdated(int level)
        {
            Dispatcher.Invoke(() =>
            {
                txtBatteryLevel.Text = $"Battery: {level}%";
                txtBatteryLevel.Visibility = Visibility.Visible;
            });
        }

        private void Bt_OnHardwareSyncReceived(HardwareAdvInfo info)
        {
            Dispatcher.Invoke(() =>
            {
                if (info.GamingMode.HasValue)
                {
                    chkGamingMode.IsChecked = info.GamingMode.Value;
                }
            });
        }

        #endregion
    }
}
