using System;

namespace SpaceKatMonitor
{
    /// <summary>
    /// Tracks the complete raw button field without interpreting descriptor
    /// padding or assigning physical-button meaning. The presentation layer
    /// chooses whether undeclared bits are allowed in compatibility mode.
    /// </summary>
    public sealed class ButtonState
    {
        private readonly long[] pressCounts = new long[32];

        public bool Known { get; private set; }
        public uint RawMask { get; private set; }
        public uint ObservedMask { get; private set; }
        public uint RisingMask { get; private set; }
        public long[] PressCounts { get { return pressCounts; } }

        public void Apply(uint mask)
        {
            uint previous = Known ? RawMask : 0;
            RisingMask = mask & ~previous;
            RawMask = mask;
            ObservedMask |= mask;
            Known = true;

            for (int bit = 0; bit < pressCounts.Length; bit++)
            {
                if ((RisingMask & (1u << bit)) != 0)
                    pressCounts[bit]++;
            }
        }

        public void Reset()
        {
            Known = false;
            RawMask = 0;
            ObservedMask = 0;
            RisingMask = 0;
            Array.Clear(pressCounts, 0, pressCounts.Length);
        }

        public static uint DisplayMask(uint raw, uint declared, bool rawCompatibility)
        {
            return rawCompatibility ? raw : raw & declared;
        }
    }
}
