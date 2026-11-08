using System;
using System.Collections.Generic;

namespace SpaceKatMonitor
{
    internal static class DecoderTests
    {
        public static string Run()
        {
            int checks = 0;
            List<string> cases = new List<string>();
            DecodedReport result;
            Check(!ReportDecoder.TryDecode(null, out result) && result == null, ref checks);
            Check(!ReportDecoder.TryDecode(new byte[0], out result), ref checks);
            Check(!ReportDecoder.TryDecode(new byte[] { 1 }, out result), ref checks);
            Check(!ReportDecoder.TryDecode(new byte[12] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, out result), ref checks);
            Check(!ReportDecoder.TryDecode(new byte[] { 3, 1 }, out result), ref checks);
            Check(!ReportDecoder.TryDecode(new byte[] { 2, 0, 0, 0, 0, 0, 0 }, out result), ref checks);
            Check(!ReportDecoder.TryDecode(new byte[] { 4, 1 }, out result), ref checks);
            cases.Add("Reject null, empty, truncated and unsupported reports");

            short[] expected = { -512, 511, -1, 0, short.MinValue, short.MaxValue };
            byte[] packet = new byte[13];
            packet[0] = 1;
            for (int i = 0; i < expected.Length; i++)
            {
                packet[1 + 2 * i] = unchecked((byte)expected[i]);
                packet[2 + 2 * i] = unchecked((byte)(expected[i] >> 8));
            }
            Check(ReportDecoder.TryDecode(packet, out result), ref checks);
            Check(result.ReportId == 1 && result.Buttons == null && result.Axes.Length == 6, ref checks);
            for (int i = 0; i < expected.Length; i++) Check(expected[i] == result.Axes[i], ref checks);
            cases.Add("Decode six little-endian signed axes, nominal and int16 limits");

            byte[] paddedAxes = new byte[32];
            Array.Copy(packet, paddedAxes, packet.Length);
            for (int i = 13; i < paddedAxes.Length; i++) paddedAxes[i] = 255;
            Check(ReportDecoder.TryDecode(paddedAxes, out result) && result.Axes[5] == 32767, ref checks);
            Check(ReportDecoder.TryDecode(new byte[] { 3, 5, 128 }, out result), ref checks);
            Check(result.Axes == null && result.Buttons.Value == 0x8005, ref checks);
            Check(ReportDecoder.TryDecode(new byte[] { 3, 1, 0, 0, 128 }, out result), ref checks);
            Check(result.Buttons.Value == 0x80000001u, ref checks);
            byte[] paddedButtons = new byte[13];
            paddedButtons[0] = 3; paddedButtons[1] = 10;
            Check(ReportDecoder.TryDecode(paddedButtons, out result) && result.Buttons.Value == 10, ref checks);
            Check(ReportDecoder.TryDecode(new byte[] { 3, 255, 255, 255, 255, 123, 45 }, out result)
                && result.Buttons.Value == uint.MaxValue, ref checks);
            cases.Add("Decode 16/32-bit masks, bit 32 and padded HID reports");

            foreach (uint mask in new uint[] { 1, 0, 8, 0, 0x80000000u, 0 })
            {
                byte[] pulse = { 3, (byte)mask, (byte)(mask >> 8), (byte)(mask >> 16), (byte)(mask >> 24) };
                Check(ReportDecoder.TryDecode(pulse, out result) && result.Buttons.Value == mask, ref checks);
            }
            cases.Add("Preserve press and release masks without synthesized button states");
            return "PASS: " + checks + " checks\r\n" + string.Join("\r\n", cases.ToArray()) + "\r\n";
        }

        private static void Check(bool passed, ref int count)
        {
            count++;
            if (!passed) throw new InvalidOperationException("Decoder assertion " + count + " failed.");
        }
    }
}
