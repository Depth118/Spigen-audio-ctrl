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

        // 10-Band EQ State
        private readonly float[] _gains = new float[10];
        private readonly Slider[] _sliders = new Slider[10];
        private readonly TextBlock[] _gainLabels = new TextBlock[10];
        private int _draggedNodeIdx = -1;

        // Fix #9 (audit): Removed duplicate EqFrequencies array; reference RcspProtocol.EqFrequencies directly.
        private static readonly float[] HarmanGains = new float[] { 4, 2, 0, 0, 1, 3, 4, 2, 1, 0 };

        // Fix #4 (audit): Debounce timer — sends EQ to device 150ms after the last slider move,
        // preventing flooding the GATT channel with writes on every tick during a drag.
        private readonly DispatcherTimer _eqDebounceTimer;

        private readonly Dictionary<string, float[]> _presets = new()
        {
            { "Harman Audiophile Target", new float[] { 4, 2, 0, 0, 1, 3, 4, 2, 1, 0 } },
            { "Default Flat", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } },
            { "Bass Boost", new float[] { 6, 4, 2, 0, -1, -2, -1, 0, 2, 3 } },
            { "Pop", new float[] { -1, 1, 2, 3, 2, 0, 1, 2, 3, 2 } },
            { "Rock", new float[] { 4, 2, -1, -2, 0, 1, 2, 3, 4, 4 } },
            { "Classical", new float[] { 3, 2, 1, 0, 0, 1, 2, 3, 2, 1 } },
            { "Vocal Enhance", new float[] { -2, -1, 0, 2, 4, 4, 3, 1, 0, -1 } },
            { "Gaming Footstep & Spatial", new float[] { 2, 0, -2, 1, 3, 4, 4, 3, 2, 3 } }
        };

        public MainWindow()
        {
            InitializeComponent();

            // Fix #4 (audit): Initialize EQ debounce timer
            _eqDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _eqDebounceTimer.Tick += (s, e) =>
            {
                _eqDebounceTimer.Stop();
                _ = _bt.SetEqualizerAsync(_gains);
            };

            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;

            _bt.OnConnectionStateChanged += Bt_OnConnectionStateChanged;
            _bt.OnHardwareSyncReceived += Bt_OnHardwareSyncReceived;
            // Fix #5 (audit): Subscribe to battery updates from both BLE service and RCSP TLV
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

        #region 10-Band Equalizer Engine

        private void InitEqualizerUI()
        {
            // Populate Presets
            cmbEqPresets.Items.Clear();
            foreach (var p in _presets.Keys)
            {
                cmbEqPresets.Items.Add(p);
            }
            cmbEqPresets.SelectedIndex = 0;

            // Build 10 Vertical Slider Columns
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

                var lblGain = new TextBlock
                {
                    Text = "+0dB",
                    Foreground = (SolidColorBrush)FindResource("NordicInkPrimary"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                _gainLabels[i] = lblGain;

                var slider = new Slider
                {
                    Style = (Style)FindResource("NordicVerticalSliderStyle"),
                    Height = 110,
                    Tag = index,
                    Value = _gains[i]
                };

                // Fix #4 (audit): Debounce — reset the 150ms timer on each tick instead of
                // calling SetEqualizerAsync directly (which floods the GATT channel during drags).
                slider.ValueChanged += (s, ev) =>
                {
                    _gains[index] = (float)Math.Round(slider.Value * 2) / 2.0f;
                    _gainLabels[index].Text = $"{(_gains[index] > 0 ? "+" : "")}{_gains[index]}dB";
                    DrawEqualizerCurve();
                    _eqDebounceTimer.Stop();
                    _eqDebounceTimer.Start();
                };
                _sliders[i] = slider;

                // Fix #9 (audit): Use RcspProtocol.EqFrequencies — removed duplicate array from MainWindow
                int freq = RcspProtocol.EqFrequencies[i];
                string freqText = (freq >= 1000) ? $"{freq / 1000.0:0.#}k" : $"{freq}";
                var lblFreq = new TextBlock
                {
                    Text = freqText,
                    Foreground = (SolidColorBrush)FindResource("NordicInkTertiary"),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                col.Children.Add(lblGain);
                col.Children.Add(slider);
                col.Children.Add(lblFreq);

                eqSlidersGrid.Children.Add(col);
            }

            // Apply default preset gains
            ApplyPreset("Harman Audiophile Target");
        }

        private void ApplyPreset(string name)
        {
            if (_presets.TryGetValue(name, out var gains))
            {
                for (int i = 0; i < 10; i++)
                {
                    _gains[i] = gains[i];
                    if (_sliders[i] != null) _sliders[i].Value = _gains[i];
                    if (_gainLabels[i] != null) _gainLabels[i].Text = $"{(_gains[i] > 0 ? "+" : "")}{_gains[i]}dB";
                }
                DrawEqualizerCurve();
                _ = _bt.SetEqualizerAsync(_gains);
            }
        }

        private void CmbEqPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbEqPresets.SelectedItem is string name)
            {
                ApplyPreset(name);
            }
        }

        // Fix #6 (audit): Use a unique name by incrementing a counter until no collision is found,
        // preventing silent overwrites when saving multiple custom presets.
        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            int n = 1;
            string customName;
            do { customName = $"Custom {n++}"; } while (_presets.ContainsKey(customName));

            _presets[customName] = (float[])_gains.Clone();
            cmbEqPresets.Items.Add(customName);
            cmbEqPresets.SelectedItem = customName;
        }

        private void BtnResetEq_Click(object sender, RoutedEventArgs e)
        {
            cmbEqPresets.SelectedItem = "Default Flat";
        }

        private void ChkHarmanTarget_Click(object sender, RoutedEventArgs e)
        {
            DrawEqualizerCurve();
        }

        private void EqCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawEqualizerCurve();

        private double GetNodeX(int index, double width)
        {
            double padding = 34;
            double usableWidth = width - (padding * 2);
            return padding + (index / 9.0) * usableWidth;
        }

        private double GetNodeY(double db, double height)
        {
            double padding = 22;
            double usableHeight = height - (padding * 2);
            double norm = (8.0 - db) / 16.0;
            return padding + (norm * usableHeight);
        }

        private double GetDbFromY(double y, double height)
        {
            double padding = 22;
            double usableHeight = height - (padding * 2);
            double clampedY = Math.Max(padding, Math.Min(height - padding, y));
            double norm = (clampedY - padding) / usableHeight;
            double db = 8.0 - (norm * 16.0);
            return Math.Round(db * 2) / 2.0;
        }

        private void DrawEqualizerCurve()
        {
            if (eqCanvas == null || eqCanvas.ActualWidth < 10) return;

            eqCanvas.Children.Clear();
            double w = eqCanvas.ActualWidth;
            double h = eqCanvas.ActualHeight;

            // 1. Draw dB Grid Horizontal Lines (Restrained Opacity)
            int[] dbLevels = new int[] { -8, -4, 0, 4, 8 };
            foreach (var db in dbLevels)
            {
                double y = GetNodeY(db, h);
                var line = new Line
                {
                    X1 = 26, Y1 = y,
                    X2 = w - 26, Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(db == 0 ? (byte)30 : (byte)15, 255, 255, 255)),
                    StrokeThickness = 1
                };
                eqCanvas.Children.Add(line);

                var lbl = new TextBlock
                {
                    Text = $"{(db > 0 ? "+" : "")}{db}dB",
                    Foreground = new SolidColorBrush(Color.FromArgb(db == 0 ? (byte)140 : (byte)70, 255, 255, 255)),
                    FontSize = 9,
                    FontFamily = (FontFamily)FindResource("SegoeFluentFont")
                };
                Canvas.SetLeft(lbl, 4);
                Canvas.SetTop(lbl, y - 6);
                eqCanvas.Children.Add(lbl);
            }

            // 2. Draw Vertical Grid Lines
            for (int i = 0; i < 10; i++)
            {
                double x = GetNodeX(i, w);
                var line = new Line
                {
                    X1 = x, Y1 = 16,
                    X2 = x, Y2 = h - 16,
                    Stroke = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                    StrokeThickness = 1
                };
                eqCanvas.Children.Add(line);
            }

            // 3. Draw Harman Target Reference Ghost Curve (Refined Gold Dashed)
            if (chkHarmanTarget.IsChecked == true)
            {
                var harmanPath = CreateSmoothBezierPath(HarmanGains, w, h);
                harmanPath.Stroke = (SolidColorBrush)FindResource("NordicGoldAccent");
                harmanPath.StrokeThickness = 1.8;
                harmanPath.StrokeDashArray = new DoubleCollection { 4, 3 };
                eqCanvas.Children.Add(harmanPath);
            }

            // 4. Draw Active EQ Curve Area Fill (Quiet Alpha Gradient)
            var areaPath = CreateFilledCurvePath(_gains, w, h);
            eqCanvas.Children.Add(areaPath);

            // 5. Draw Active EQ Solid White Curve (2.0px)
            var activePath = CreateSmoothBezierPath(_gains, w, h);
            activePath.Stroke = Brushes.White;
            activePath.StrokeThickness = 2.0;
            eqCanvas.Children.Add(activePath);

            // 6. Draw Interactive Handle Nodes (Monochrome)
            for (int i = 0; i < 10; i++)
            {
                double x = GetNodeX(i, w);
                double y = GetNodeY(_gains[i], h);

                var outer = new Ellipse
                {
                    Width = 12, Height = 12,
                    Fill = Brushes.White
                };
                Canvas.SetLeft(outer, x - 6);
                Canvas.SetTop(outer, y - 6);
                eqCanvas.Children.Add(outer);

                var inner = new Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = (SolidColorBrush)FindResource("NordicCanvasBrush")
                };
                Canvas.SetLeft(inner, x - 3);
                Canvas.SetTop(inner, y - 3);
                eqCanvas.Children.Add(inner);
            }
        }

        private Path CreateSmoothBezierPath(float[] gains, double w, double h)
        {
            var path = new Path();
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(GetNodeX(0, w), GetNodeY(gains[0], h)) };

            Point[] points = new Point[10];
            for (int i = 0; i < 10; i++)
            {
                points[i] = new Point(GetNodeX(i, w), GetNodeY(gains[i], h));
            }

            for (int i = 0; i < 9; i++)
            {
                Point p0 = (i == 0) ? points[i] : points[i - 1];
                Point p1 = points[i];
                Point p2 = points[i + 1];
                Point p3 = (i + 2 < 10) ? points[i + 2] : points[i + 1];

                Point cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                Point cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
            }

            geometry.Figures.Add(figure);
            path.Data = geometry;
            return path;
        }

        private Path CreateFilledCurvePath(float[] gains, double w, double h)
        {
            var path = new Path();
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(GetNodeX(0, w), GetNodeY(gains[0], h)) };

            Point[] points = new Point[10];
            for (int i = 0; i < 10; i++)
            {
                points[i] = new Point(GetNodeX(i, w), GetNodeY(gains[i], h));
            }

            for (int i = 0; i < 9; i++)
            {
                Point p0 = (i == 0) ? points[i] : points[i - 1];
                Point p1 = points[i];
                Point p2 = points[i + 1];
                Point p3 = (i + 2 < 10) ? points[i + 2] : points[i + 1];

                Point cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                Point cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);

                figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
            }

            figure.Segments.Add(new LineSegment(new Point(GetNodeX(9, w), GetNodeY(-8, h)), true));
            figure.Segments.Add(new LineSegment(new Point(GetNodeX(0, w), GetNodeY(-8, h)), true));
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

        private void EqCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(eqCanvas);
            double w = eqCanvas.ActualWidth;

            // Find closest band node
            int closest = -1;
            double minDist = 30;
            for (int i = 0; i < 10; i++)
            {
                double x = GetNodeX(i, w);
                double dist = Math.Abs(pos.X - x);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = i;
                }
            }

            if (closest != -1)
            {
                _draggedNodeIdx = closest;
                eqCanvas.CaptureMouse();
                UpdateNodeFromPointer(pos.Y);
            }
        }

        private void EqCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedNodeIdx != -1)
            {
                var pos = e.GetPosition(eqCanvas);
                UpdateNodeFromPointer(pos.Y);
            }
        }

        private void EqCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedNodeIdx != -1)
            {
                _draggedNodeIdx = -1;
                eqCanvas.ReleaseMouseCapture();
                // Canvas drag commits on MouseUp only — no debounce needed here
                _ = _bt.SetEqualizerAsync(_gains);
            }
        }

        private void UpdateNodeFromPointer(double mouseY)
        {
            if (_draggedNodeIdx == -1) return;
            double db = GetDbFromY(mouseY, eqCanvas.ActualHeight);
            _gains[_draggedNodeIdx] = (float)db;
            if (_sliders[_draggedNodeIdx] != null)
            {
                _sliders[_draggedNodeIdx].Value = db;
            }
            if (_gainLabels[_draggedNodeIdx] != null)
            {
                _gainLabels[_draggedNodeIdx].Text = $"{(db > 0 ? "+" : "")}{db}dB";
            }
            DrawEqualizerCurve();
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
