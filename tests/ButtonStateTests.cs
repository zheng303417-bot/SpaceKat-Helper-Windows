using System;
using System.Collections.Generic;

namespace SpaceKatMonitor
{
    public static class ButtonStateTests
    {
        public static string Run()
        {
            int checks = 0;
            List<string> cases = new List<string>();
            ButtonState state = new ButtonState();

            Check(!state.Known && state.RawMask == 0 && state.ObservedMask == 0 &&
                state.RisingMask == 0 && state.PressCounts.Length == 32, ref checks);
            Check(AllCountsZero(state.PressCounts), ref checks);

            // The descriptor's declared mask is deliberately not an input to
            // raw state tracking. Bits 3 and 4 must remain available to inspect.
            const uint declared = 3;
            state.Apply(4);
            Check(state.Known && state.RawMask == 4 && state.ObservedMask == 4 &&
                state.RisingMask == 4 && state.PressCounts[2] == 1, ref checks);
            Check(ButtonState.DisplayMask(state.RawMask, declared, false) == 0, ref checks);
            Check(ButtonState.DisplayMask(state.RawMask, declared, true) == 4, ref checks);
            Check(state.RawMask == 4 && state.PressCounts[2] == 1, ref checks);
            state.Apply(0);
            state.Apply(8);
            Check(state.RawMask == 8 && state.ObservedMask == 12 && state.RisingMask == 8 &&
                state.PressCounts[3] == 1, ref checks);
            Check(ButtonState.DisplayMask(state.RawMask, declared, false) == 0 &&
                ButtonState.DisplayMask(state.RawMask, declared, true) == 8, ref checks);
            cases.Add("Keep raw bits 3/4 with declared mask 0x03; apply filtering only for display");

            state.Reset();
            state.Apply(1);
            state.Apply(1);
            Check(state.RisingMask == 0 && state.PressCounts[0] == 1, ref checks);
            state.Apply(0);
            Check(state.Known && state.RawMask == 0 && state.RisingMask == 0 &&
                state.ObservedMask == 1 && state.PressCounts[0] == 1, ref checks);
            // Pulses are applied as received, with no dependence on a UI tick.
            state.Apply(1);
            state.Apply(0);
            state.Apply(1);
            state.Apply(0);
            Check(state.PressCounts[0] == 3 && state.RawMask == 0 && state.ObservedMask == 1,
                ref checks);
            cases.Add("Count press edges once, retain releases and capture short pulses");

            state.Reset();
            state.Apply(0x80000009u);
            Check(state.RawMask == 0x80000009u && state.RisingMask == 0x80000009u &&
                state.PressCounts[0] == 1 && state.PressCounts[3] == 1 &&
                state.PressCounts[31] == 1, ref checks);
            state.Apply(0x80000009u);
            Check(state.RisingMask == 0 && state.PressCounts[31] == 1, ref checks);
            state.Apply(0x80000000u);
            Check(state.RawMask == 0x80000000u && state.RisingMask == 0, ref checks);
            state.Apply(0x80000004u);
            Check(state.RisingMask == 4 && state.PressCounts[2] == 1 &&
                state.PressCounts[31] == 1 && state.ObservedMask == 0x8000000Du, ref checks);
            state.Apply(0);
            state.Apply(0x80000000u);
            Check(state.PressCounts[31] == 2 && state.RisingMask == 0x80000000u, ref checks);
            cases.Add("Handle simultaneous presses, partial release and unsigned bit 32");

            long[] existingCounts = state.PressCounts;
            state.Reset();
            Check(!state.Known && state.RawMask == 0 && state.ObservedMask == 0 &&
                state.RisingMask == 0, ref checks);
            Check(Object.ReferenceEquals(existingCounts, state.PressCounts) &&
                AllCountsZero(existingCounts), ref checks);
            state.Apply(0);
            Check(state.Known && state.RawMask == 0 && state.ObservedMask == 0 &&
                state.RisingMask == 0 && AllCountsZero(state.PressCounts), ref checks);
            state.Apply(0xFFFFFFFFu);
            Check(state.RisingMask == 0xFFFFFFFFu && state.ObservedMask == 0xFFFFFFFFu,
                ref checks);
            for (int bit = 0; bit < 32; bit++)
                Check(state.PressCounts[bit] == 1, ref checks);
            Check(ButtonState.DisplayMask(state.RawMask, declared, false) == declared &&
                ButtonState.DisplayMask(state.RawMask, declared, true) == UInt32.MaxValue,
                ref checks);
            cases.Add("Reset all stored state; count initially asserted bits once");

            return "PASS: " + checks + " button state checks\r\n" +
                String.Join("\r\n", cases.ToArray()) + "\r\n";
        }

        private static bool AllCountsZero(long[] counts)
        {
            foreach (long count in counts)
                if (count != 0) return false;
            return true;
        }

        private static void Check(bool passed, ref int count)
        {
            count++;
            if (!passed)
                throw new InvalidOperationException("Button state assertion " + count + " failed.");
        }
    }
}
