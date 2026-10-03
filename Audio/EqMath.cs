using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpigenAudioCTRL.Audio
{
    // Frequency response of the headset's EQ, modelled as cascaded RBJ peaking biquads
    // (the filter type used by the stock music EQ).
    public static class EqMath
    {
        public const double SampleRate = 44100.0;
        public const float MinGain = -10.0f;
        public const float MaxGain = 8.0f;
        public const float MinQ = 0.2f;
        public const float MaxQ = 5.0f;
        public const int MinFrequency = 20;
        public const int MaxFrequency = 20000;

        public static double PeakingResponseDb(double frequency, double centre, double gainDb, double q)
        {
            if (Math.Abs(gainDb) < 1e-6 || centre <= 0 || q <= 0 || centre >= SampleRate / 2) return 0.0;

            double a = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * centre / SampleRate;
            double alpha = Math.Sin(w0) / (2.0 * q);
            double cos = Math.Cos(w0);

            var z1 = Complex.Exp(new Complex(0, -2.0 * Math.PI * frequency / SampleRate));
            var z2 = z1 * z1;
            var numerator = (1 + alpha * a) - 2 * cos * z1 + (1 - alpha * a) * z2;
            var denominator = (1 + alpha / a) - 2 * cos * z1 + (1 - alpha / a) * z2;
            return 20.0 * Math.Log10((numerator / denominator).Magnitude);
        }

        public static double ResponseDb(IEnumerable<EqBand> bands, double frequency)
        {
            double total = 0.0;
            foreach (var band in bands) total += PeakingResponseDb(frequency, band.Frequency, band.Gain, band.Q);
            return total;
        }

        public static double PeakDb(IReadOnlyList<EqBand> bands)
        {
            const int points = 600;
            double peak = double.NegativeInfinity;
            for (int i = 0; i < points; i++)
            {
                double frequency = LogFrequency(i / (double)(points - 1));
                peak = Math.Max(peak, ResponseDb(bands, frequency));
            }
            return peak;
        }

        // Pre-attenuation that keeps the combined curve at or below 0 dB, rounded up to
        // 0.5 dB. This reproduces the -5.5 dB Spigen uses for its own default tuning.
        public static float HeadroomGain(IReadOnlyList<EqBand> bands)
        {
            double peak = PeakDb(bands);
            if (peak <= 0.0) return 0.0f;
            return -(float)(Math.Ceiling(peak * 2.0 - 1e-6) / 2.0);
        }

        // Maps 0..1 onto 20 Hz..20 kHz logarithmically.
        public static double LogFrequency(double position) =>
            MinFrequency * Math.Pow(MaxFrequency / (double)MinFrequency, Math.Clamp(position, 0.0, 1.0));

        public static double LogPosition(double frequency) =>
            Math.Log(Math.Clamp(frequency, MinFrequency, MaxFrequency) / MinFrequency) /
            Math.Log(MaxFrequency / (double)MinFrequency);

        public static EqBand Clamp(EqBand band) => new(
            Math.Clamp(band.Frequency, MinFrequency, MaxFrequency),
            Math.Clamp(band.Gain, MinGain, MaxGain),
            Math.Clamp(band.Q, MinQ, MaxQ));
    }
}
