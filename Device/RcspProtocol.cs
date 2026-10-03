using System;
using System.Collections.Generic;
using System.Text;
using SpigenAudioCTRL.Audio;

namespace SpigenAudioCTRL.Device
{
    // Jieli RCSP framing as used by the SA-HP P10 over BLE:
    //   FE DC BA [flags] [opcode] [len_hi] [len_lo] [body...] EF
    // flags bit 7 = command (0 = response), bit 6 = response expected.
    // Command body:  [sn] [payload...]
    // Response body: [status] [sn] [payload...]
    // Layouts below were confirmed against the official app's traffic captured
    // in the phone's HCI log and its decompiled RCSP library.
    public static class RcspOpcode
    {
        public const byte SetAdvInfo = 0xC0;
        public const byte GetAdvInfo = 0xC1;
        public const byte SystemOperation = 0xC5;
        public const byte Custom = 0xFF;
    }

    // Value types of the ADV info TLVs (opcode 0xC0 / 0xC1).
    public static class AdvInfoType
    {
        public const byte Battery = 0;
        public const byte Name = 1;
        public const byte KeySettings = 2;
        public const byte WorkMode = 5;
        public const byte InEar = 8;
        public const byte Language = 9;
        public const byte SleepMode = 10;
        public const byte VolumeBalance = 12;

        public static uint Mask(params byte[] types)
        {
            uint mask = 0;
            foreach (var t in types) mask |= 1u << t;
            return mask;
        }
    }

    // Arguments of the system operation command (opcode 0xC5).
    public static class SystemOperation
    {
        public const byte RestoreSettings = 1;
        public const byte ClearPairedRecords = 2;
    }

    // Sub-types of the vendor custom command (opcode 0xFF).
    public static class CustomType
    {
        public const byte Read = 0xFE;
        public const byte SleepTimer = 20;
        public const byte Anc = 23;
        public const byte Eq = 32;
    }

    public sealed class RcspFrame
    {
        public byte Flags { get; init; }
        public byte Opcode { get; init; }
        public byte? Status { get; init; }
        public byte Sequence { get; init; }
        public byte[] Payload { get; init; } = Array.Empty<byte>();

        public bool IsCommand => (Flags & 0x80) != 0;
    }

    // Reassembles RCSP frames from BLE notifications, which may split or coalesce frames.
    public sealed class RcspFrameParser
    {
        private const int MaxBodyLength = 4096;
        private readonly List<byte> _buffer = new();

        public List<RcspFrame> Push(byte[] chunk)
        {
            _buffer.AddRange(chunk);
            var frames = new List<RcspFrame>();

            while (true)
            {
                int start = FindHeader();
                if (start < 0)
                {
                    // Keep a possible partial header at the end of the buffer.
                    int keep = Math.Min(2, _buffer.Count);
                    _buffer.RemoveRange(0, _buffer.Count - keep);
                    break;
                }
                if (start > 0) _buffer.RemoveRange(0, start);
                if (_buffer.Count < 7) break;

                int length = (_buffer[5] << 8) | _buffer[6];
                if (length > MaxBodyLength)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }
                int total = 8 + length;
                if (_buffer.Count < total) break;
                if (_buffer[total - 1] != 0xEF)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }

                var frame = BuildFrame(_buffer.GetRange(0, total).ToArray(), length);
                _buffer.RemoveRange(0, total);
                if (frame != null) frames.Add(frame);
            }

            return frames;
        }

        private int FindHeader()
        {
            for (int i = 0; i + 2 < _buffer.Count; i++)
            {
                if (_buffer[i] == 0xFE && _buffer[i + 1] == 0xDC && _buffer[i + 2] == 0xBA) return i;
            }
            return -1;
        }

        private static RcspFrame? BuildFrame(byte[] packet, int length)
        {
            byte flags = packet[3];
            bool isCommand = (flags & 0x80) != 0;
            int headerBytes = isCommand ? 1 : 2;
            if (length < headerBytes) return null;

            int payloadStart = 7 + headerBytes;
            var payload = new byte[length - headerBytes];
            Array.Copy(packet, payloadStart, payload, 0, payload.Length);

            return new RcspFrame
            {
                Flags = flags,
                Opcode = packet[4],
                Status = isCommand ? null : packet[7],
                Sequence = isCommand ? packet[7] : packet[8],
                Payload = payload
            };
        }
    }

    public static class RcspProtocol
    {
        public const byte CustomEqMode = 126;

        public static byte[] PackCommand(byte opcode, byte sn, byte[] payload, bool expectResponse = true)
        {
            byte flags = expectResponse ? (byte)0xC0 : (byte)0x80;
            int bodyLength = payload.Length + 1;
            var packet = new byte[8 + bodyLength];
            packet[0] = 0xFE;
            packet[1] = 0xDC;
            packet[2] = 0xBA;
            packet[3] = flags;
            packet[4] = opcode;
            packet[5] = (byte)(bodyLength >> 8);
            packet[6] = (byte)bodyLength;
            packet[7] = sn;
            Array.Copy(payload, 0, packet, 8, payload.Length);
            packet[^1] = 0xEF;
            return packet;
        }

        public static byte[] EncodeGetAdvInfo(uint mask, byte sn)
        {
            // The mask is sent big-endian.
            var payload = new[] { (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask };
            return PackCommand(RcspOpcode.GetAdvInfo, sn, payload);
        }

        public static byte[] EncodeSetAdvInfo(byte type, byte[] value, byte sn)
        {
            var payload = new byte[value.Length + 2];
            payload[0] = (byte)(value.Length + 1);
            payload[1] = type;
            Array.Copy(value, 0, payload, 2, value.Length);
            return PackCommand(RcspOpcode.SetAdvInfo, sn, payload);
        }

        public static byte[] EncodeGamingMode(bool enabled, byte sn) =>
            EncodeSetAdvInfo(AdvInfoType.WorkMode, new[] { enabled ? (byte)2 : (byte)1 }, sn);

        public static byte[] EncodeKeyMapping(byte key, byte action, byte function, byte sn) =>
            EncodeSetAdvInfo(AdvInfoType.KeySettings, new[] { key, action, KeyFunction.ToDevice(function) }, sn);

        public static byte[] EncodeBalance(int value, byte sn) =>
            EncodeSetAdvInfo(AdvInfoType.VolumeBalance, new[] { (byte)Math.Clamp(value, 0, 100) }, sn);

        // Minutes until the headset powers off; SleepTimerState.Off disables the timer.
        // The headset treats 0 as "power off now", so it is rejected here.
        public static byte[] EncodeSleepTimer(int minutes, byte sn)
        {
            if (minutes <= 0 || minutes > SleepTimerState.Off) throw new ArgumentOutOfRangeException(nameof(minutes));
            var payload = new byte[] { CustomType.SleepTimer, 4, (byte)minutes, (byte)(minutes >> 8), 0, 0 };
            return PackCommand(RcspOpcode.Custom, sn, payload);
        }

        public static byte[] EncodeSystemOperation(byte operation, byte sn) =>
            PackCommand(RcspOpcode.SystemOperation, sn, new[] { operation });

        public static byte[] EncodeCustomRead(byte type, byte sn) =>
            PackCommand(RcspOpcode.Custom, sn, new byte[] { CustomType.Read, 0x01, type });

        public static byte[] EncodeAnc(AncSetting setting, byte sn)
        {
            byte scene = setting.Scene;
            // Quirk carried over from the vendor app: mode 3 / scene 1 / level 0 is sent as scene 2.
            if (setting.Mode == 3 && scene == 1 && setting.Level == 0) scene = 2;
            var payload = new byte[] { CustomType.Anc, 3, setting.Mode, scene, setting.Level };
            return PackCommand(RcspOpcode.Custom, sn, payload);
        }

        public static byte[] EncodeEqPayload(EqProfile profile)
        {
            var bands = profile.Bands;
            var payload = new byte[5 + bands.Count * 6];
            payload[0] = CustomType.Eq;
            payload[1] = (byte)(3 + bands.Count * 6);
            payload[2] = profile.Mode;
            WriteInt16(payload, 3, ToCentis(profile.MasterGain));

            for (int i = 0; i < bands.Count; i++)
            {
                int offset = 5 + i * 6;
                WriteInt16(payload, offset, (short)bands[i].Frequency);
                WriteInt16(payload, offset + 2, ToCentis(bands[i].Gain));
                WriteInt16(payload, offset + 4, ToCentis(bands[i].Q));
            }
            return payload;
        }

        public static byte[] EncodeEq(EqProfile profile, byte sn) =>
            PackCommand(RcspOpcode.Custom, sn, EncodeEqPayload(profile));

        // Custom replies carry [type] [length] [data...].
        public static (byte Type, byte[] Data)? ParseCustomReply(byte[] payload)
        {
            if (payload.Length < 2 || payload.Length != payload[1] + 2) return null;
            var data = new byte[payload[1]];
            Array.Copy(payload, 2, data, 0, data.Length);
            return (payload[0], data);
        }

        public static EqProfile? ParseEq(byte[] data)
        {
            if (data.Length < 9 || (data.Length - 3) % 6 != 0) return null;

            var bands = new List<EqBand>();
            for (int offset = 3; offset < data.Length; offset += 6)
            {
                bands.Add(new EqBand(
                    ReadUInt16(data, offset),
                    ReadInt16(data, offset + 2) / 100.0f,
                    ReadUInt16(data, offset + 4) / 100.0f));
            }

            return new EqProfile(bands, ReadInt16(data, 1) / 100.0f, data[0]);
        }

        public static SleepTimerState? ParseSleepTimer(byte[] data) =>
            data.Length >= 4 ? new SleepTimerState(ReadUInt16(data, 0), ReadUInt16(data, 2)) : null;

        public static AncSetting? ParseAnc(byte[] data) =>
            data.Length >= 3 ? new AncSetting(data[0], data[1], data[2]) : null;

        public static AdvInfo ParseAdvInfo(byte[] payload)
        {
            var info = new AdvInfo();
            int ptr = 0;
            while (ptr + 2 <= payload.Length)
            {
                int length = payload[ptr];
                if (length < 2 || ptr + 1 + length > payload.Length) break;

                byte type = payload[ptr + 1];
                var value = new byte[length - 1];
                Array.Copy(payload, ptr + 2, value, 0, value.Length);
                ptr += length + 1;

                switch (type)
                {
                    case AdvInfoType.Battery:
                        // Bit 7 = charging, low 7 bits = percent (first byte is the headset itself).
                        info.BatteryLevel = value[0] & 0x7F;
                        info.IsCharging = (value[0] & 0x80) != 0;
                        break;
                    case AdvInfoType.Name:
                        info.Name = Encoding.UTF8.GetString(value).TrimEnd('\0');
                        break;
                    case AdvInfoType.KeySettings:
                        for (int k = 0; k + 3 <= value.Length; k += 3)
                        {
                            info.KeySettings.Add(new KeySetting(value[k], value[k + 1], value[k + 2]));
                        }
                        break;
                    case AdvInfoType.WorkMode:
                        info.WorkMode = value[0];
                        break;
                    case AdvInfoType.VolumeBalance:
                        info.VolumeBalance = value[0];
                        break;
                    case AdvInfoType.Language:
                        info.Language = Encoding.ASCII.GetString(value).TrimEnd('\0');
                        break;
                }
            }
            return info;
        }

        private static short ToCentis(float value) => (short)Math.Round(value * 100.0f);

        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static short ReadInt16(byte[] buffer, int offset) =>
            (short)(buffer[offset] | (buffer[offset + 1] << 8));

        private static int ReadUInt16(byte[] buffer, int offset) =>
            buffer[offset] | (buffer[offset + 1] << 8);
    }

    // ANC is addressed as mode / scene / level. The vendor config encodes the same
    // value as a 24-bit id, e.g. 0x010102 = noise cancelling, "noisy" scene, level 2.
    public readonly record struct AncSetting(byte Mode, byte Scene, byte Level)
    {
        public int Id => (Mode << 16) | (Scene << 8) | Level;

        public static AncSetting FromId(int id) => new((byte)(id >> 16), (byte)(id >> 8), (byte)id);
    }

    public sealed record KeySetting(byte Key, byte Action, byte Function);

    // Sleep timer as reported by the headset: the configured minutes (Off when disabled)
    // and a non-zero second field while a countdown is running.
    public readonly record struct SleepTimerState(int Minutes, int Remaining)
    {
        public const int Off = 0xFFFF;

        public bool IsActive => Minutes != Off && Remaining > 0;
    }

    public static class KeyFunction
    {
        // The headset stores "disabled" as 127; the vendor app shows it as 0.
        public const byte DeviceDisabled = 127;
        // Reported for gestures the headset does not support.
        public const byte Unsupported = 0x80;

        public static byte ToDevice(byte function) => function == 0 ? DeviceDisabled : function;

        public static byte FromDevice(byte function) => function == DeviceDisabled ? (byte)0 : function;
    }

    public sealed class AdvInfo
    {
        public int? BatteryLevel { get; set; }
        public bool IsCharging { get; set; }
        public string? Name { get; set; }
        public int? WorkMode { get; set; }
        public int? VolumeBalance { get; set; }
        public string? Language { get; set; }
        public List<KeySetting> KeySettings { get; } = new();

        public bool? GamingMode => WorkMode.HasValue ? WorkMode.Value == 2 : null;
    }

    public sealed class EqProfile
    {
        public IReadOnlyList<EqBand> Bands { get; }
        public float MasterGain { get; }
        public byte Mode { get; }

        public EqProfile(IReadOnlyList<EqBand> bands, float masterGain, byte mode = RcspProtocol.CustomEqMode)
        {
            Bands = bands;
            MasterGain = masterGain;
            Mode = mode;
        }
    }
}
