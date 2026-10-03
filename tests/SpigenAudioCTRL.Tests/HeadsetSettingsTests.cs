using SpigenAudioCTRL.Device;
using Xunit;

namespace SpigenAudioCTRL.Tests;

// Balance, sleep timer, system operations and ANC mode lookup.
// Fixtures are replies captured from an SA-HP P10 talking to Spigen's app.
public class HeadsetSettingsTests
{
    private const string SleepTimerReply = "FEDCBA00FF000800381404FFFF0000EF";
    private const string BalanceReply = "FEDCBA00C10005003A020C32EF";

    private static RcspFrame Single(byte[] bytes) => Assert.Single(new RcspFrameParser().Push(bytes));

    private static RcspFrame Single(string hex) => Single(Convert.FromHexString(hex));

    [Fact]
    public void ParseAdvInfo_ReadsCapturedBalance()
    {
        var info = RcspProtocol.ParseAdvInfo(Single(BalanceReply).Payload);
        Assert.Equal(50, info.VolumeBalance);
    }

    [Fact]
    public void EncodeBalance_WritesAdvInfoTypeTwelveAndClamps()
    {
        Assert.Equal(new byte[] { 2, AdvInfoType.VolumeBalance, 30 }, Single(RcspProtocol.EncodeBalance(30, 1)).Payload);
        Assert.Equal(new byte[] { 2, AdvInfoType.VolumeBalance, 100 }, Single(RcspProtocol.EncodeBalance(140, 1)).Payload);
    }

    [Fact]
    public void ParseSleepTimer_ReadsCapturedDisabledTimer()
    {
        var reply = RcspProtocol.ParseCustomReply(Single(SleepTimerReply).Payload)!.Value;
        Assert.Equal(CustomType.SleepTimer, reply.Type);

        var timer = RcspProtocol.ParseSleepTimer(reply.Data);
        Assert.Equal(SleepTimerState.Off, timer!.Value.Minutes);
        Assert.False(timer.Value.IsActive);
    }

    [Fact]
    public void EncodeSleepTimer_UsesVendorLayout()
    {
        Assert.Equal(new byte[] { 20, 4, 30, 0, 0, 0 }, Single(RcspProtocol.EncodeSleepTimer(30, 1)).Payload);
        Assert.Equal(new byte[] { 20, 4, 0xFF, 0xFF, 0, 0 }, Single(RcspProtocol.EncodeSleepTimer(SleepTimerState.Off, 1)).Payload);
    }

    [Fact]
    public void EncodeSleepTimer_RefusesZeroBecauseItPowersOffImmediately()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RcspProtocol.EncodeSleepTimer(0, 1));
    }

    [Fact]
    public void EncodeSystemOperation_UsesOpcodeC5()
    {
        var frame = Single(RcspProtocol.EncodeSystemOperation(SystemOperation.RestoreSettings, 7));
        Assert.Equal(RcspOpcode.SystemOperation, frame.Opcode);
        Assert.Equal(new byte[] { 1 }, frame.Payload);

        Assert.Equal(new byte[] { 2 }, Single(RcspProtocol.EncodeSystemOperation(SystemOperation.ClearPairedRecords, 8)).Payload);
    }

    [Fact]
    public void AncModes_FindVendorSettings()
    {
        Assert.Same(AncModes.Deep, AncModes.Find(AncSetting.FromId(65794)));
        Assert.Same(AncModes.Adaptive, AncModes.Find(AncSetting.FromId(66816)));
        Assert.Same(AncModes.Indoor, AncModes.Find(AncSetting.FromId(66304)));
        Assert.Same(AncModes.Transparency, AncModes.Find(new AncSetting(3, 2, 0)));
        Assert.Same(AncModes.Off, AncModes.Find(AncSetting.FromId(131072)));
        Assert.Null(AncModes.Find(new AncSetting(9, 9, 9)));

        Assert.Equal(new AncSetting(1, 2, 1), AncModes.Commuting.ToSetting(1));
        Assert.Equal(new AncSetting(3, 1, 4), AncModes.Transparency.ToSetting(1));
    }
}
