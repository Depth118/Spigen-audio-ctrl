using SpigenAudioCTRL.Audio;
using Xunit;

namespace SpigenAudioCTRL.Tests;

public class EqTests
{
    [Theory]
    [InlineData(1000, 6.0, 1.0)]
    [InlineData(220, -10.0, 1.2)]
    [InlineData(60, 4.0, 0.4)]
    public void PeakingFilter_ReachesItsGainAtTheCentre(int centre, double gain, double q)
    {
        Assert.Equal(gain, EqMath.PeakingResponseDb(centre, centre, gain, q), 2);
    }

    [Fact]
    public void PeakingFilter_IsNeutralFarFromTheCentre()
    {
        Assert.InRange(EqMath.PeakingResponseDb(20, 4000, 6.0, 1.5), -0.05, 0.05);
        Assert.Equal(0.0, EqMath.PeakingResponseDb(1000, 1000, 0.0, 1.0));
    }

    [Fact]
    public void Headroom_MatchesSpigensOwnPreAmpForStockTuning()
    {
        // Spigen ships its default tuning with -5.5 dB master gain.
        Assert.Equal(-5.5f, EqMath.HeadroomGain(EqPresets.Stock.Bands));
    }

    [Fact]
    public void Headroom_AccountsForOverlappingBoosts()
    {
        var bands = new[] { new EqBand(2000, 3f, 1.4f), new EqBand(2500, 3.5f, 1.2f) };
        // Each band alone is <= 3.5 dB, but together they peak higher.
        Assert.True(EqMath.PeakDb(bands) > 4.5);
        Assert.True(EqMath.HeadroomGain(bands) <= -5.0f);
    }

    [Fact]
    public void Headroom_IsZeroWithoutBoosts()
    {
        Assert.Equal(0f, EqMath.HeadroomGain(new[] { new EqBand(220, -6f, 1.2f), new EqBand(1000, 0f, 1f) }));
    }

    [Fact]
    public void LogFrequency_RoundTrips()
    {
        foreach (var f in new[] { 20.0, 60.0, 1000.0, 7500.0, 20000.0 })
        {
            Assert.Equal(f, EqMath.LogFrequency(EqMath.LogPosition(f)), 6);
        }
    }

    [Fact]
    public void Presets_AreValidForTheHeadset()
    {
        Assert.Equal(EqPresets.BuiltIn.Count, EqPresets.BuiltIn.Select(p => p.Name).Distinct().Count());
        foreach (var preset in EqPresets.BuiltIn)
        {
            Assert.Equal(10, preset.Bands.Length);
            Assert.All(preset.Bands, b =>
            {
                Assert.InRange(b.Gain, EqMath.MinGain, EqMath.MaxGain);
                Assert.InRange(b.Q, EqMath.MinQ, EqMath.MaxQ);
                Assert.InRange(b.Frequency, EqMath.MinFrequency, EqMath.MaxFrequency);
            });
        }
    }

    [Fact]
    public void TunedPresets_KeepSpigensLowMidCorrection()
    {
        double stock = EqMath.ResponseDb(EqPresets.Stock.Bands, 220);
        var tuned = EqPresets.BuiltIn.Where(p => p.Name is EqPresets.SignatureName or "Airy" or "Warm" or "Relaxed" or "Vocal" or "Gaming").ToList();
        Assert.Equal(6, tuned.Count);
        foreach (var preset in tuned)
        {
            Assert.InRange(EqMath.ResponseDb(preset.Bands, 220) - stock, -1.0, 2.5);
        }
    }

    [Fact]
    public void FindMatch_RecognisesPresetsWithinDeviceRounding()
    {
        var fromDevice = EqPresets.Stock.Bands.Select(b => new EqBand(b.Frequency, b.Gain + 0.01f, b.Q)).ToArray();
        Assert.Same(EqPresets.Stock, EqPresets.FindMatch(EqPresets.BuiltIn, fromDevice));

        fromDevice[1].Gain = -2.5f;
        Assert.Null(EqPresets.FindMatch(EqPresets.BuiltIn, fromDevice));
    }
}
