using System;
using System.Collections.Generic;
using System.Linq;

namespace SpigenAudioCTRL.Audio
{
    // One aspect of the sound the tuner asks about, and how strongly each EQ band takes part in it.
    public sealed record TunerDimension(string Name, string Description, float[] Shape);

    public enum TunerChoice { A, B, CantTell }

    // A/B preference test, in the style of the "find your sound" features in OEM apps.
    // The listener plays their own music while the headset switches between two versions.
    // For each dimension the first round decides "less or more", the second "how much".
    public sealed class SoundTuner
    {
        public const float FirstStep = 3.0f;
        public const float SmallStep = 1.5f;
        public const float LargeStep = 4.5f;

        // Weights per band, in the order of EqPresets.Frequencies (60 Hz ... 16 kHz).
        public static IReadOnlyList<TunerDimension> Dimensions { get; } = new[]
        {
            new TunerDimension("Bass", "kick drums and bass lines", new[] { 1.0f, 0.35f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }),
            new TunerDimension("Body", "fullness of voices and guitars", new[] { 0f, 0.5f, 1.0f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f }),
            new TunerDimension("Clarity", "voices and lead instruments", new[] { 0f, 0f, 0f, 0.5f, 1.0f, 0.6f, 0f, 0f, 0f, 0f }),
            new TunerDimension("Brightness", "cymbals and \"s\" sounds", new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1.0f, 0.8f, 0f, 0f }),
            new TunerDimension("Air", "sparkle and sense of space", new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1.0f, 0.8f }),
        };

        public static int MaxSteps => Dimensions.Count * 2;

        private readonly IReadOnlyList<EqBand> _baseBands;
        private readonly Random _random;
        private readonly float[] _amounts = new float[Dimensions.Count];
        private float _direction;

        public int DimensionIndex { get; private set; }
        public int Round { get; private set; }
        public int Step { get; private set; } = 1;
        public bool IsComplete => DimensionIndex >= Dimensions.Count;
        public TunerDimension Dimension => Dimensions[DimensionIndex];
        public IReadOnlyList<float> Amounts => _amounts;

        // Amounts (dB at the shape's peak) for the current A and B; order is randomized.
        public float AmountA { get; private set; }
        public float AmountB { get; private set; }

        public SoundTuner(IReadOnlyList<EqBand> baseBands, Random? random = null)
        {
            _baseBands = baseBands;
            _random = random ?? new Random();
            PreparePair();
        }

        public EqBand[] CandidateA => Build(DimensionIndex, AmountA);

        public EqBand[] CandidateB => Build(DimensionIndex, AmountB);

        public void Choose(TunerChoice choice)
        {
            if (IsComplete) throw new InvalidOperationException("The tuner is already complete.");

            float chosen = choice switch
            {
                TunerChoice.A => AmountA,
                TunerChoice.B => AmountB,
                _ => Round == 0 ? 0f : _direction * FirstStep
            };

            if (Round == 0 && choice != TunerChoice.CantTell)
            {
                _direction = Math.Sign(chosen);
                Round = 1;
            }
            else
            {
                _amounts[DimensionIndex] = chosen;
                DimensionIndex++;
                Round = 0;
            }

            Step++;
            if (!IsComplete) PreparePair();
        }

        public EqBand[] Result() => Apply(_baseBands, _amounts);

        public static EqBand[] Apply(IReadOnlyList<EqBand> baseBands, IReadOnlyList<float> amounts)
        {
            var result = baseBands.Select(b => b.Clone()).ToArray();
            for (int d = 0; d < Dimensions.Count; d++)
            {
                for (int i = 0; i < result.Length; i++) result[i].Gain += Dimensions[d].Shape[i] * amounts[d];
            }
            foreach (var band in result)
            {
                band.Gain = Math.Clamp((float)(Math.Round(band.Gain * 2) / 2.0), EqMath.MinGain, EqMath.MaxGain);
            }
            return result;
        }

        private EqBand[] Build(int dimension, float amount)
        {
            var amounts = _amounts.ToArray();
            amounts[dimension] = amount;
            return Apply(_baseBands, amounts);
        }

        private void PreparePair()
        {
            (float first, float second) = Round == 0
                ? (-FirstStep, FirstStep)
                : (_direction * SmallStep, _direction * LargeStep);

            bool swap = _random.Next(2) == 1;
            AmountA = swap ? second : first;
            AmountB = swap ? first : second;
        }
    }
}
