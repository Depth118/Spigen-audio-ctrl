using System;
using System.Collections.Generic;
using System.Linq;

namespace SpigenAudioCTRL.Audio
{
    public sealed class EqPreset
    {
        public string Name { get; }
        public string Description { get; }
        public EqBand[] Bands { get; }
        public bool IsCustom { get; }

        public EqPreset(string name, string description, EqBand[] bands, bool isCustom = false)
        {
            Name = name;
            Description = description;
            Bands = bands;
            IsCustom = isCustom;
        }

        public bool Matches(IReadOnlyList<EqBand> bands)
        {
            if (bands.Count != Bands.Length) return false;
            for (int i = 0; i < Bands.Length; i++)
            {
                if (bands[i].Frequency != Bands[i].Frequency ||
                    Math.Abs(bands[i].Gain - Bands[i].Gain) > 0.05f ||
                    Math.Abs(bands[i].Q - Bands[i].Q) > 0.02f)
                {
                    return false;
                }
            }
            return true;
        }
    }

    // The headset's custom EQ replaces Spigen's built-in voicing rather than stacking on it:
    // every preset in the official app re-applies the same correction (a deep cut around
    // 220 Hz plus a presence lift) on this 10-band layout. Presets here therefore start from
    // that correction instead of from 0 dB.
    public static class EqPresets
    {
        public const string StockName = "Spigen Default (stock tuning)";
        public const string SignatureName = "Signature";

        public static readonly int[] Frequencies = { 60, 220, 500, 1000, 2000, 2500, 5000, 7500, 12000, 16000 };
        public static readonly float[] DefaultQ = { 0.4f, 1.2f, 1.5f, 1.5f, 1.2f, 0.8f, 1.2f, 1.2f, 1.2f, 1.2f };

        // Spigen's default tuning (official app config for the H3/P10), mapped onto the 10-band layout.
        private static readonly float[] StockGains = { 4f, -10f, 1f, -1f, 2f, 3.5f, 0f, 3f, 0f, 0f };

        public static IReadOnlyList<EqPreset> BuiltIn { get; } = new List<EqPreset>
        {
            Vendor(StockName,
                "Spigen's own correction for this driver: a deep 220 Hz cut against boominess, with a presence lift. Recommended starting point.",
                StockGains, DefaultQ),
            OnStock(SignatureName,
                "Spigen's correction, refined: fuller sub-bass, smoother 2-5 kHz, tamed 7.5 kHz sibilance and a little air. About 1 dB from stock everywhere.",
                new[] { 1f, 1f, 0f, 0f, 0f, -1f, 0f, -1.5f, 1.5f, 1f }),
            Vendor("Spigen Pop", "Spigen preset: stock tuning with less sub-bass.",
                new[] { -2f, -10f, 1f, -1f, 2f, 3.5f, 0f, 3f, 0f, 0f }, DefaultQ),
            Vendor("Spigen Bass", "Spigen preset: heavier bass, relaxed low-mid cut, softer treble.",
                new[] { 6f, -3.5f, 1f, -1f, -3.5f, 0f, 0f, -4f, 0f, 0f },
                new[] { 0.4f, 1.2f, 1.5f, 1.5f, 0.9f, 0.7f, 1.2f, 0.5f, 1.2f, 1.2f }),
            Vendor("Spigen Rock", "Spigen preset: stock tuning with wider filters and more bass.",
                new[] { 5.5f, -10f, 0f, -1f, 1.5f, 3.5f, 0f, 3f, 0f, 0f },
                new[] { 0.4f, 0.7f, 0.7f, 1.5f, 0.7f, 0.8f, 1.2f, 0.7f, 1.2f, 1.2f }),
            Vendor("Spigen Soft", "Spigen preset: stock tuning with gentler bass and treble.",
                new[] { 0f, -10f, 1f, -1f, 2f, 2f, 0f, 2f, 0f, 0f },
                new[] { 0.7f, 1.2f, 1.5f, 1.5f, 1.2f, 0.8f, 1.2f, 1.2f, 1.2f, 1.2f }),
            Vendor("Spigen Classic", "Spigen preset: lighter low-mid cut for a fuller midrange.",
                new[] { 5f, -5f, 1f, 0f, 2f, 2f, 0f, 3f, 0f, 0f },
                new[] { 0.4f, 1.5f, 1.5f, 0.7f, 1.2f, 0.8f, 1.2f, 1.2f, 1.2f, 1.2f }),

            OnStock("Airy", "Spigen's tuning with extra sparkle above 10 kHz.",
                new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 2f, 2f }),
            OnStock("Warm", "Spigen's tuning with fuller low-mids and smoother treble.",
                new[] { 1f, 2f, 0f, 0f, 0f, 0f, 0f, -1.5f, -1f, 0f }),
            OnStock("Relaxed", "Spigen's tuning with softer upper mids and treble for long sessions.",
                new[] { 0f, 0f, 0f, 0f, 0f, -1.5f, -1f, -2f, -1.5f, 0f }),
            OnStock("Vocal", "Spigen's tuning with clearer voices and a little less sub-bass.",
                new[] { -1f, 0f, 0f, 1f, 1f, 0f, 0f, -1f, 0f, 0f }),
            OnStock("Gaming", "Spigen's tuning for games: less rumble, more 2-5 kHz detail for footsteps.",
                new[] { -2f, 0f, -1f, 0f, 1f, 0f, 2.5f, 0f, 0f, 0f }),

            Vendor("Raw Driver (no correction)",
                "Bypasses Spigen's correction entirely. Expect boomy, muddy low-mids; useful only for comparison.",
                new float[10], DefaultQ),
        };

        public static EqPreset Stock => BuiltIn[0];

        public static EqPreset? FindMatch(IEnumerable<EqPreset> presets, IReadOnlyList<EqBand> bands) =>
            presets.FirstOrDefault(p => p.Matches(bands));

        private static EqPreset Vendor(string name, string description, float[] gains, float[] q) =>
            new(name, description, Build(gains, q));

        private static EqPreset OnStock(string name, string description, float[] offsets)
        {
            var gains = new float[Frequencies.Length];
            for (int i = 0; i < gains.Length; i++) gains[i] = StockGains[i] + offsets[i];
            return new EqPreset(name, description, Build(gains, DefaultQ));
        }

        private static EqBand[] Build(float[] gains, float[] q)
        {
            var bands = new EqBand[Frequencies.Length];
            for (int i = 0; i < bands.Length; i++) bands[i] = new EqBand(Frequencies[i], gains[i], q[i]);
            return bands;
        }
    }
}
