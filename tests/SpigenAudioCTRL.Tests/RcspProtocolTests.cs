using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Device;
using Xunit;

namespace SpigenAudioCTRL.Tests;

// Fixtures are frames exchanged between Spigen's official app and an SA-HP P10,
// taken from an Android HCI log (no addresses or serials included).
public class RcspProtocolTests
{
    private const string EqReadRequest = "FEDCBAC0FF000436FE0120EF";
    private const string EqReadReply =
        "FEDCBA00FF00430036203F7EA2FE3C005E013200DC0006FF7800F4016AFF8C00E80300008C00D00796008200" +
        "C4092C0178008813C80082004C1D6AFFB400E02E2C017800803EC8006400EF";
    private const string AdvInfoReply =
        "FEDCBA00C1009F003704001400000B0153706967656E20503130020501020C32020A02790207010107020307" +
        "030407040007050007818007828007838007848007858003010603020003030003040003050203818003828003" +
        "838003848003858005010505020005030005040005050305818005828005838005848005858009010A09020009" +
        "030009040009050009817F09828009838009848009858002080005094A4C3030EF";
    private const string AncReadRequest = "FEDCBAC0FF00043EFE0117EF";
    private const string AncReadReply = "FEDCBA00FF0007003E1703010102EF";
    private const string AdvPollRequest = "FEDCBAC0C100053500000D21EF";
    private const string SetAdvInfoAck = "FEDCBA00C00003003400EF";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static RcspFrame Single(string hex)
    {
        var frames = new RcspFrameParser().Push(Hex(hex));
        return Assert.Single(frames);
    }

    [Fact]
    public void Parser_ReassemblesFramesSplitAcrossNotifications()
    {
        var stream = Hex("00FF" + EqReadReply + AncReadReply + SetAdvInfoAck);
        var parser = new RcspFrameParser();
        var frames = new List<RcspFrame>();
        for (int offset = 0; offset < stream.Length; offset += 20)
        {
            frames.AddRange(parser.Push(stream[offset..Math.Min(offset + 20, stream.Length)]));
        }

        Assert.Equal(3, frames.Count);
        Assert.Equal(new byte[] { 0x36, 0x3E, 0x34 }, frames.Select(f => f.Sequence));
        Assert.All(frames, f => Assert.Equal((byte)0, f.Status));
        Assert.False(frames[0].IsCommand);
        // 0x43 body bytes minus status and sequence: type, length, mode, master gain, 10 x 6 band bytes.
        Assert.Equal(65, frames[0].Payload.Length);
    }

    [Fact]
    public void Parser_ResynchronisesAfterCorruptFrame()
    {
        var corrupt = Hex(AncReadReply);
        corrupt[^1] = 0x00;
        var frames = new RcspFrameParser().Push(corrupt.Concat(Hex(SetAdvInfoAck)).ToArray());

        var frame = Assert.Single(frames);
        Assert.Equal(RcspOpcode.SetAdvInfo, frame.Opcode);
    }

    [Fact]
    public void Parser_ReadsCommandFrames()
    {
        var frame = Single(EqReadRequest);
        Assert.True(frame.IsCommand);
        Assert.Null(frame.Status);
        Assert.Equal(0x36, frame.Sequence);
        Assert.Equal(new byte[] { 0xFE, 0x01, 0x20 }, frame.Payload);
    }

    [Fact]
    public void ReadRequests_MatchOfficialApp()
    {
        Assert.Equal(EqReadRequest, Convert.ToHexString(RcspProtocol.EncodeCustomRead(CustomType.Eq, 0x36)));
        Assert.Equal(AncReadRequest, Convert.ToHexString(RcspProtocol.EncodeCustomRead(CustomType.Anc, 0x3E)));
        Assert.Equal(AdvPollRequest, Convert.ToHexString(RcspProtocol.EncodeGetAdvInfo(0x0D21, 0x35)));
    }

    [Fact]
    public void ParseEq_ReadsCapturedHeadsetCurve()
    {
        var reply = RcspProtocol.ParseCustomReply(Single(EqReadReply).Payload);
        Assert.NotNull(reply);
        Assert.Equal(CustomType.Eq, reply!.Value.Type);

        var eq = RcspProtocol.ParseEq(reply.Value.Data);
        Assert.NotNull(eq);
        Assert.Equal(RcspProtocol.CustomEqMode, eq!.Mode);
        Assert.Equal(-3.5f, eq.MasterGain, 3);
        Assert.Equal(10, eq.Bands.Count);
        Assert.Equal(new[] { 60, 220, 500, 1000, 2000, 2500, 5000, 7500, 12000, 16000 }, eq.Bands.Select(b => b.Frequency));
        Assert.Equal(-2.5f, eq.Bands[1].Gain, 3);
        Assert.Equal(1.2f, eq.Bands[1].Q, 3);
        Assert.Equal(3.5f, eq.Bands[0].Gain, 3);
    }

    [Fact]
    public void EncodeEq_ReproducesCapturedBytes()
    {
        var payload = Single(EqReadReply).Payload;
        var eq = RcspProtocol.ParseEq(RcspProtocol.ParseCustomReply(payload)!.Value.Data)!;

        Assert.Equal(payload, RcspProtocol.EncodeEqPayload(eq));
    }

    [Fact]
    public void EncodeEq_RoundTripsThroughParser()
    {
        var bands = EqPresets.Stock.Bands;
        var profile = new EqProfile(bands, EqMath.HeadroomGain(bands));
        var frame = Single(Convert.ToHexString(RcspProtocol.EncodeEq(profile, 9)));

        Assert.Equal(RcspOpcode.Custom, frame.Opcode);
        var parsed = RcspProtocol.ParseEq(RcspProtocol.ParseCustomReply(frame.Payload)!.Value.Data)!;
        Assert.Equal(profile.MasterGain, parsed.MasterGain, 3);
        for (int i = 0; i < bands.Length; i++)
        {
            Assert.Equal(bands[i].Frequency, parsed.Bands[i].Frequency);
            Assert.Equal(bands[i].Gain, parsed.Bands[i].Gain, 3);
            Assert.Equal(bands[i].Q, parsed.Bands[i].Q, 3);
        }
    }

    [Fact]
    public void ParseAdvInfo_ReadsCapturedStatus()
    {
        var info = RcspProtocol.ParseAdvInfo(Single(AdvInfoReply).Payload);

        Assert.Equal(20, info.BatteryLevel);
        Assert.False(info.IsCharging);
        Assert.Equal("Spigen P10", info.Name);
        Assert.False(info.GamingMode);
        Assert.Equal("JL00", info.Language);
        Assert.Equal(40, info.KeySettings.Count);
        Assert.Contains(new KeySetting(7, 1, 1), info.KeySettings);
        Assert.Contains(new KeySetting(7, 3, 4), info.KeySettings);
        Assert.Contains(new KeySetting(5, 5, 3), info.KeySettings);
        Assert.Contains(new KeySetting(3, 5, 2), info.KeySettings);
        Assert.Contains(new KeySetting(7, 0x81, KeyFunction.Unsupported), info.KeySettings);
    }

    [Fact]
    public void ParseAdvInfo_ReadsChargingFlag()
    {
        var info = RcspProtocol.ParseAdvInfo(new byte[] { 0x04, 0x00, 0xD5, 0x00, 0x00 });
        Assert.Equal(85, info.BatteryLevel);
        Assert.True(info.IsCharging);
    }

    [Fact]
    public void ParseAnc_ReadsCapturedMode()
    {
        var reply = RcspProtocol.ParseCustomReply(Single(AncReadReply).Payload)!.Value;
        Assert.Equal(CustomType.Anc, reply.Type);

        var anc = RcspProtocol.ParseAnc(reply.Data);
        Assert.Equal(new AncSetting(1, 1, 2), anc);
        Assert.Equal(65794, anc!.Value.Id);
    }

    [Fact]
    public void EncodeAnc_UsesVendorLayout()
    {
        var frame = Single(Convert.ToHexString(RcspProtocol.EncodeAnc(AncSetting.FromId(65794), 1)));
        Assert.Equal(new byte[] { 23, 3, 1, 1, 2 }, frame.Payload);

        var transparency = Single(Convert.ToHexString(RcspProtocol.EncodeAnc(AncSetting.FromId(196868), 2)));
        Assert.Equal(new byte[] { 23, 3, 3, 1, 4 }, transparency.Payload);
    }

    [Fact]
    public void EncodeKeyMapping_SendsDisabledAs127()
    {
        var frame = Single(Convert.ToHexString(RcspProtocol.EncodeKeyMapping(7, 3, 0, 5)));
        Assert.Equal(RcspOpcode.SetAdvInfo, frame.Opcode);
        Assert.Equal(new byte[] { 4, AdvInfoType.KeySettings, 7, 3, 127 }, frame.Payload);
        Assert.Equal(0, KeyFunction.FromDevice(127));
    }

    [Fact]
    public void EncodeGamingMode_UsesWorkModeValues()
    {
        Assert.Equal(new byte[] { 2, AdvInfoType.WorkMode, 2 }, Single(Convert.ToHexString(RcspProtocol.EncodeGamingMode(true, 1))).Payload);
        Assert.Equal(new byte[] { 2, AdvInfoType.WorkMode, 1 }, Single(Convert.ToHexString(RcspProtocol.EncodeGamingMode(false, 1))).Payload);
    }
}
