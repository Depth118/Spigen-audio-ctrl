using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Device;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL.Views
{
    // "Find your sound": A/B comparisons on the listener's own music, applied live on the headset.
    public partial class PersonalEqWindow : Window
    {
        private readonly HeadsetController _headset;
        private readonly PresetLibrary _presets;
        private readonly IReadOnlyList<EqBand>? _originalEq;
        private SoundTuner? _tuner;
        private EqBand[]? _result;
        private bool _heardA;
        private bool _heardB;
        private bool _changedHeadset;
        private bool _saved;
        private bool _rendering;

        public PersonalEqWindow(HeadsetController headset, PresetLibrary presets)
        {
            InitializeComponent();
            _headset = headset;
            _presets = presets;
            _originalEq = headset.Eq?.Select(b => b.Clone()).ToArray();

            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            Closed += OnClosed;
        }

        private void Start_Click(object sender, RoutedEventArgs e) => StartTuner();

        private void StartOver_Click(object sender, RoutedEventArgs e) => StartTuner();

        private void StartTuner()
        {
            _tuner = new SoundTuner(EqPresets.Stock.Bands);
            _result = null;
            ShowStep();
        }

        private void ShowStep()
        {
            var tuner = _tuner!;
            introPanel.Visibility = Visibility.Collapsed;
            resultPanel.Visibility = Visibility.Collapsed;
            comparePanel.Visibility = Visibility.Visible;

            txtProgress.Text = $"Step {tuner.Step} of up to {SoundTuner.MaxSteps}";
            txtDimension.Text = tuner.Dimension.Name;
            txtDimensionHint.Text = tuner.Round == 0
                ? $"Listen to the {tuner.Dimension.Description}."
                : $"Nearly there for {tuner.Dimension.Name.ToLowerInvariant()}: which amount do you prefer?";

            _heardA = _heardB = false;
            _rendering = true;
            optionA.IsChecked = false;
            optionB.IsChecked = false;
            _rendering = false;
            txtStateA.Text = txtStateB.Text = "Tap to listen";
            txtHint.Text = "Listen to both, switching as often as you like.";
            btnChoose.Content = "Choose";
            btnChoose.IsEnabled = false;
            optionA.Focus();
        }

        private async void Option_Checked(object sender, RoutedEventArgs e)
        {
            if (_rendering || _tuner == null) return;
            bool isA = sender == optionA;
            if (isA) _heardA = true;
            else _heardB = true;

            txtStateA.Text = isA ? "Listening" : _heardA ? "Heard" : "Tap to listen";
            txtStateB.Text = !isA ? "Listening" : _heardB ? "Heard" : "Tap to listen";
            btnChoose.Content = isA ? "Choose A" : "Choose B";
            btnChoose.IsEnabled = _heardA && _heardB;
            if (!btnChoose.IsEnabled) txtHint.Text = $"Now try {(isA ? "B" : "A")}.";
            else txtHint.Text = "Pick the one you prefer, or keep switching.";

            _changedHeadset = true;
            await _headset.SetEqAsync(isA ? _tuner.CandidateA : _tuner.CandidateB);
        }

        private void Choose_Click(object sender, RoutedEventArgs e) =>
            Advance(optionA.IsChecked == true ? TunerChoice.A : TunerChoice.B);

        private void CantTell_Click(object sender, RoutedEventArgs e) => Advance(TunerChoice.CantTell);

        private void Advance(TunerChoice choice)
        {
            if (_tuner == null) return;
            _tuner.Choose(choice);
            if (_tuner.IsComplete) ShowResult();
            else ShowStep();
        }

        private async void ShowResult()
        {
            var tuner = _tuner!;
            _result = tuner.Result();

            var changes = new List<string>();
            for (int i = 0; i < SoundTuner.Dimensions.Count; i++)
            {
                float amount = tuner.Amounts[i];
                if (amount == 0) continue;
                string size = Math.Abs(amount) <= SoundTuner.SmallStep ? "a little " : Math.Abs(amount) >= SoundTuner.LargeStep ? "much " : "";
                changes.Add($"{size}{(amount > 0 ? "more" : "less")} {SoundTuner.Dimensions[i].Name.ToLowerInvariant()}");
            }
            txtSummary.Text = changes.Count == 0
                ? "You preferred Spigen's tuning as it is."
                : $"Compared with Spigen's tuning, you like {string.Join(", ", changes)}.";

            comparePanel.Visibility = Visibility.Collapsed;
            resultPanel.Visibility = Visibility.Visible;
            DrawResult();

            _changedHeadset = true;
            await _headset.SetEqAsync(_result);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null) return;
            _presets.Save(_result, PresetLibrary.PersonalName);
            _saved = true;
            await _headset.SetEqAsync(_result);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_changedHeadset && !_saved && _originalEq != null) _ = _headset.SetEqAsync(_originalEq);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (comparePanel.Visibility != Visibility.Visible) return;
            switch (e.Key)
            {
                case Key.A:
                    optionA.IsChecked = true;
                    break;
                case Key.B:
                    optionB.IsChecked = true;
                    break;
                case Key.Enter when btnChoose.IsEnabled:
                    Choose_Click(btnChoose, new RoutedEventArgs());
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void ResultCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawResult();

        private void DrawResult()
        {
            if (_result == null || resultCanvas.ActualWidth < 10) return;
            resultCanvas.Children.Clear();
            double w = resultCanvas.ActualWidth;
            double h = resultCanvas.ActualHeight;
            const double top = 10, bottom = -12;

            double Y(double db) => Math.Clamp(8 + (top - db) / (top - bottom) * (h - 16), 8, h - 8);
            double X(double f) => 12 + EqMath.LogPosition(f) * (w - 24);

            resultCanvas.Children.Add(new Line
            {
                X1 = 12, X2 = w - 12, Y1 = Y(0), Y2 = Y(0),
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 1
            });
            resultCanvas.Children.Add(Curve(EqPresets.Stock.Bands, X, Y, (Brush)FindResource("NordicGoldAccent"), dashed: true));
            resultCanvas.Children.Add(Curve(_result, X, Y, Brushes.White, dashed: false));
        }

        private static Polyline Curve(IReadOnlyList<EqBand> bands, Func<double, double> x, Func<double, double> y, Brush stroke, bool dashed)
        {
            var line = new Polyline { Stroke = stroke, StrokeThickness = dashed ? 1.6 : 2.0 };
            if (dashed) line.StrokeDashArray = new DoubleCollection { 4, 3 };
            for (int s = 0; s <= 120; s++)
            {
                double f = EqMath.LogFrequency(s / 120.0);
                line.Points.Add(new Point(x(f), y(EqMath.ResponseDb(bands, f))));
            }
            return line;
        }
    }
}
