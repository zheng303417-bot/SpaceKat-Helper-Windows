using System;

namespace SpaceKatMonitor
{
    public sealed class DecodedReport
    {
        public byte ReportId;
        public short[] Axes;
        public uint? Buttons;
    }

    public static class ReportDecoder
    {
        // SpaceKat Mini II v2.0.1: report 1 contains all six axes, report 3
        // contains a button bitfield. Raw Input may pad shorter reports to the
        // collection's maximum input length, so trailing bytes are ignored.
        public static bool TryDecode(byte[] report, out DecodedReport decoded)
        {
            decoded = null;
            if (report == null || report.Length == 0) return false;
            if (report[0] == 1)
            {
                if (report.Length < 13) return false;
                short[] axes = new short[6];
                for (int i = 0; i < axes.Length; i++)
                {
                    int offset = 1 + i * 2;
                    axes[i] = unchecked((short)(report[offset] | (report[offset + 1] << 8)));
                }
                decoded = new DecodedReport { ReportId = 1, Axes = axes };
                return true;
            }
            if (report[0] == 3)
            {
                // Both verified descriptors provide at least 16 button bits;
                // the 32-button descriptor supplies four payload bytes.
                if (report.Length < 3) return false;
                uint mask = 0;
                for (int i = 1; i < report.Length && i <= 4; i++)
                    mask |= (uint)report[i] << ((i - 1) * 8);
                decoded = new DecodedReport { ReportId = 3, Buttons = mask };
                return true;
            }
            return false;
        }
    }
}
