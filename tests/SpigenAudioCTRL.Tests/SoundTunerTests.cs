using SpigenAudioCTRL.Audio;
using Xunit;

namespace SpigenAudioCTRL.Tests;

public class SoundTunerTests
{
    private static SoundTuner NewTuner(int seed = 1) => new(EqPresets.Stock.Bands, new Random(seed));

    // Picks whichever option has the wanted sign (round 1) or size (round 2).
    private static void Pick(SoundTuner tuner, Func<float, float, bool> prefersFirst) =>
        tuner.Choose(prefersFirst(tuner.AmountA, tuner.AmountB) ? TunerChoice.A : TunerChoice.B);

    [Fact]
    public void CantTellEverywhere_KeepsTheBaseCurveInFiveSteps()
    {
        var tuner = NewTuner();
        while (!tuner.IsComplete) tuner.Choose(TunerChoice.CantTell);

        Assert.Equal(SoundTuner.Dimensions.Count + 1, tuner.Step);
        Assert.All(tuner.Amounts, a => Assert.Equal(0f, a));
        var result = tuner.Result();
        for (int i = 0; i < result.Length; i++) Assert.Equal(EqPresets.Stock.Bands[i].Gain, result[i].Gain);
    }

    [Fact]
    public void FirstRoundComparesLessWithMore()
    {
        var tuner = NewTuner();
        Assert.Equal(new[] { -SoundTuner.FirstStep, SoundTuner.FirstStep }, new[] { tuner.AmountA, tuner.AmountB }.OrderBy(a => a));
    }

    [Fact]
    public void SecondRoundRefinesTheChosenDirection()
    {
        var tuner = NewTuner();
        Pick(tuner, (a, b) => a > b); // "more"

        Assert.Equal(0, tuner.DimensionIndex);
        Assert.Equal(1, tuner.Round);
        Assert.Equal(new[] { SoundTuner.SmallStep, SoundTuner.LargeStep }, new[] { tuner.AmountA, tuner.AmountB }.OrderBy(a => a));

        Pick(tuner, (a, b) => a > b); // "a lot more"
        Assert.Equal(SoundTuner.LargeStep, tuner.Amounts[0]);
        Assert.Equal(1, tuner.DimensionIndex);
    }

    [Fact]
    public void CantTellInSecondRound_KeepsTheFirstStep()
    {
        var tuner = NewTuner();
        Pick(tuner, (a, b) => a < b); // "less"
        tuner.Choose(TunerChoice.CantTell);

        Assert.Equal(-SoundTuner.FirstStep, tuner.Amounts[0]);
    }

    [Fact]
    public void FullSession_TakesAtMostMaxSteps()
    {
        var tuner = NewTuner(7);
        int choices = 0;
        while (!tuner.IsComplete)
        {
            Pick(tuner, (a, b) => a > b);
            choices++;
        }
        Assert.Equal(SoundTuner.MaxSteps, choices);
        Assert.All(tuner.Amounts, a => Assert.Equal(SoundTuner.LargeStep, a));
    }

    [Fact]
    public void OptionOrderIsRandomized()
    {
        var firstIsLess = Enumerable.Range(0, 40).Select(seed => NewTuner(seed).AmountA < 0).ToList();
        Assert.Contains(true, firstIsLess);
        Assert.Contains(false, firstIsLess);
    }

    [Fact]
    public void Candidates_ChangeOnlyTheBandsOfTheCurrentDimension()
    {
        var tuner = NewTuner();
        var a = tuner.CandidateA;
        var b = tuner.CandidateB;
        var bass = SoundTuner.Dimensions[0].Shape;

        for (int i = 0; i < a.Length; i++)
        {
            if (bass[i] == 0) Assert.Equal(a[i].Gain, b[i].Gain);
        }
        Assert.NotEqual(a[0].Gain, b[0].Gain);
    }

    [Fact]
    public void Result_StaysWithinTheHeadsetLimits()
    {
        var extreme = Enumerable.Repeat(SoundTuner.LargeStep, SoundTuner.Dimensions.Count).ToArray();
        var boosted = SoundTuner.Apply(EqPresets.Stock.Bands, extreme);
        var cut = SoundTuner.Apply(EqPresets.Stock.Bands, extreme.Select(a => -a).ToArray());

        Assert.All(boosted.Concat(cut), b => Assert.InRange(b.Gain, EqMath.MinGain, EqMath.MaxGain));
        Assert.True(EqMath.ResponseDb(boosted, 60) > EqMath.ResponseDb(EqPresets.Stock.Bands, 60) + 2);
        Assert.True(EqMath.ResponseDb(cut, 12000) < EqMath.ResponseDb(EqPresets.Stock.Bands, 12000) - 2);
    }

    [Fact]
    public void Shapes_CoverEveryBand()
    {
        Assert.All(SoundTuner.Dimensions, d => Assert.Equal(EqPresets.Frequencies.Length, d.Shape.Length));
    }
}
