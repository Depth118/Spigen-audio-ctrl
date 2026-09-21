using System;
using System.Collections.Generic;
using System.Text;

namespace SpigenAudioCTRL
{
    public static class RcspProtocol
    {
        public static byte[] PackRcsp(byte opcode, byte[] payload, byte sn = 1, bool hasResponse = true)
        {
            byte flag = hasResponse ? (byte)0xC0 : (byte)0x80;
            byte[] body = new byte[payload.Length + 1];
            body[0] = sn;
            Array.Copy(payload, 0, body, 1, payload.Length);

            ushort paramLen = (ushort)body.Length;
            List<byte> pkt = new List<byte>
            {
                0xFE, 0xDC, 0xBA, flag, opcode,
                (byte)((paramLen >> 8) & 0xFF),
                (byte)(paramLen & 0xFF)
            };
            pkt.AddRange(body);
            pkt.Add(0xEF);
            return pkt.ToArray();
        }

        public static byte[] EncodeAncCommand(int ancId, byte sn = 1)
        {
            byte b1 = (byte)((ancId >> 16) & 0xFF);
            byte b2 = (byte)((ancId >> 8) & 0xFF);
            byte b3 = (byte)(ancId & 0xFF);

            if (b1 == 3 && b2 == 1 && b3 == 0)
            {
                b2 = 2;
            }

            byte[] payload = new byte[] { 23, 3, b1, b2, b3 };
            return PackRcsp(0xFF, payload, sn: sn);
        }

        public static byte[] EncodeGamingMode(bool enabled, byte sn = 1)
        {
            byte val = enabled ? (byte)2 : (byte)1;
            byte[] ltv = new byte[] { 2, 5, val };
            return PackRcsp(0xC0, ltv, sn: sn);
        }

        public static byte[] EncodeKeyMapping(int keyNum, int action, int func, byte sn = 1)
        {
            byte jlFunc = (func == 0) ? (byte)127 : (byte)func;
            byte[] ltv = new byte[] { 4, 2, (byte)(keyNum & 0xFF), (byte)(action & 0xFF), jlFunc };
            return PackRcsp(0xC0, ltv, sn: sn);
        }

        public static readonly int[] EqFrequencies = new int[] { 60, 220, 500, 1000, 2000, 2500, 5000, 7500, 12000, 16000 };
        public static readonly float[] EqQValues = new float[] { 0.4f, 1.2f, 1.5f, 1.5f, 1.2f, 0.8f, 1.2f, 1.2f, 1.2f, 1.2f };

        public static float CalculateAntiClippingPreAmp(IEnumerable<float> gains)
        {
            float maxGain = 0.0f;
            foreach (var g in gains)
            {
                if (g > maxGain) maxGain = g;
            }
            // If any frequency is boosted above 0dB, apply negative pre-amp to prevent digital clipping
            return maxGain > 0.0f ? -maxGain : 0.0f;
        }

        public static float CalculateAntiClippingPreAmp(IEnumerable<EqBand> bands)
        {
            float maxGain = 0.0f;
            foreach (var b in bands)
            {
                if (b.Gain > maxGain) maxGain = b.Gain;
            }
            return maxGain > 0.0f ? -maxGain : 0.0f;
        }

        public static byte[] EncodeEqCommand(EqBand[] bands, byte mode = 126, float masterGain = 0.0f, byte sn = 1)
        {
            int totalBands = 10;
            // 65 bytes: 1 (0x20) + 1 (len=63) + 1 (mode=126) + 2 (masterGain) + (10 * 6 = 60 bytes)
            byte[] bArr = new byte[(totalBands * 6) + 5];
            bArr[0] = 32; // 0x20
            bArr[1] = (byte)((totalBands * 6) + 3); // 63
            bArr[2] = mode;

            short mg = (short)(masterGain * 100.0f);
            bArr[3] = (byte)(mg & 0xFF);
            bArr[4] = (byte)((mg >> 8) & 0xFF);

            for (int i = 0; i < totalBands; i++)
            {
                int freq = (bands != null && i < bands.Length) ? bands[i].Frequency : ((i < EqFrequencies.Length) ? EqFrequencies[i] : 1000);
                float gain = (bands != null && i < bands.Length) ? bands[i].Gain : 0.0f;
                float q = (bands != null && i < bands.Length) ? bands[i].Q : ((i < EqQValues.Length) ? EqQValues[i] : 1.2f);

                short gVal = (short)(gain * 100.0f);
                short qVal = (short)(q * 100.0f);

                int offset = (i * 6) + 5;
                bArr[offset] = (byte)(freq & 0xFF);
                bArr[offset + 1] = (byte)((freq >> 8) & 0xFF);

                bArr[offset + 2] = (byte)(gVal & 0xFF);
                bArr[offset + 3] = (byte)((gVal >> 8) & 0xFF);

                bArr[offset + 4] = (byte)(qVal & 0xFF);
                bArr[offset + 5] = (byte)((qVal >> 8) & 0xFF);
            }

            return PackRcsp(0xFF, bArr, sn: sn);
        }

        public static byte[] EncodeEqCommand(float[] gains, byte mode = 126, float masterGain = 0.0f, byte sn = 1)
        {
            var bands = new EqBand[10];
            for (int i = 0; i < 10; i++)
            {
                bands[i] = new EqBand
                {
                    Frequency = (i < EqFrequencies.Length) ? EqFrequencies[i] : 1000,
                    Gain = (gains != null && i < gains.Length) ? gains[i] : 0.0f,
                    Q = (i < EqQValues.Length) ? EqQValues[i] : 1.2f
                };
            }
            return EncodeEqCommand(bands, mode, masterGain, sn);
        }

        public static HardwareAdvInfo ParseHardwareAdvInfo(byte[] data)
        {
            var info = new HardwareAdvInfo();
            if (data == null || data.Length < 14) return info;

            int ptr = 13;
            while (ptr < data.Length - 2)
            {
                int tagLen = data[ptr];
                if (tagLen <= 0 || ptr + 1 >= data.Length) break;
                byte tag = data[ptr + 1];
                int valLen = tagLen - 1;
                int valStart = ptr + 2;

                if (valStart + valLen > data.Length) break;

                byte[] val = new byte[valLen];
                Array.Copy(data, valStart, val, 0, valLen);
                ptr += (tagLen + 1);

                if (tag == 1)
                {
                    info.DeviceName = Encoding.UTF8.GetString(val).TrimEnd('\0');
                }
                else if (tag == 2)
                {
                    for (int k = 0; k <= val.Length - 3; k += 3)
                    {
                        info.KeySettings[$"{val[k]}_{val[k + 1]}"] = val[k + 2];
                    }
                }
                else if (tag == 5)
                {
                    if (val.Length > 0) info.GamingMode = (val[0] == 2);
                }
                else if (tag == 9)
                {
                    info.Firmware = Encoding.UTF8.GetString(val).TrimEnd('\0');
                }
            }

            return info;
        }
    }

    public class HardwareAdvInfo
    {
        public string? DeviceName { get; set; }
        public string? Firmware { get; set; }
        public bool? GamingMode { get; set; }
        public Dictionary<string, int> KeySettings { get; set; } = new();
    }

    public class EqBand
    {
        public int Frequency { get; set; } = 1000;
        public float Gain { get; set; } = 0.0f;
        public float Q { get; set; } = 1.2f;

        public EqBand() { }

        public EqBand(int freq, float gain, float q = 1.2f)
        {
            Frequency = freq;
            Gain = gain;
            Q = q;
        }

        public EqBand Clone() => new EqBand(Frequency, Gain, Q);
    }
}
