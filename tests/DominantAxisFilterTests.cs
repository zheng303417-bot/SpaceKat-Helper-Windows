using System;

namespace SpaceKatMonitor
{
    internal static class DominantAxisFilterTests
    {
        public static string Run()
        {
            int count = 0;
            DominantAxisFilter filter = new DominantAxisFilter();

            // An early Z spike is ignored when sideways movement follows.
            Expect(filter, 0, .8, 0, DominantAxisGroup.None, ref count);
            Expect(filter, .6, .35, .010, DominantAxisGroup.Movement, ref count);
            Expect(filter, .3, .9, .050, DominantAxisGroup.Movement, ref count);
            Expect(filter, .01, .9, .080, DominantAxisGroup.Movement, ref count);

            // Both groups must remain near center. Brief returns do not release the lock.
            Expect(filter, .04, .04, .100, DominantAxisGroup.Movement, ref count);
            Expect(filter, .01, .07, .125, DominantAxisGroup.Movement, ref count);
            Expect(filter, .065, .065, .150, DominantAxisGroup.Movement, ref count);
            Expect(filter, 0, 0, .189, DominantAxisGroup.Movement, ref count);
            Expect(filter, 0, 0, .191, DominantAxisGroup.None, ref count);

            // A clearly dominant Z is selected after a dwell, even with some movement present.
            Expect(filter, .1, .125, .210, DominantAxisGroup.None, ref count);
            Expect(filter, .1, .125, .210, DominantAxisGroup.None, ref count);
            Expect(filter, .1, .125, .239, DominantAxisGroup.None, ref count);
            Expect(filter, .1, .125, .241, DominantAxisGroup.Z, ref count);
            Expect(filter, .9, .1, .260, DominantAxisGroup.Z, ref count);
            Expect(filter, .07, 0, .290, DominantAxisGroup.Z, ref count);
            Expect(filter, 0, 0, .310, DominantAxisGroup.Z, ref count);
            Expect(filter, 0, 0, .351, DominantAxisGroup.None, ref count);
            Expect(filter, .25, .31, .360, DominantAxisGroup.Movement, ref count);

            filter.Reset();
            Expect(filter, .079, .119, 0, DominantAxisGroup.None, ref count);
            Expect(filter, .08, .119, .010, DominantAxisGroup.Movement, ref count);

            filter.Reset();
            Expect(filter, 0, .12, 0, DominantAxisGroup.None, ref count);
            Expect(filter, .02, .1, .020, DominantAxisGroup.None, ref count);
            Expect(filter, 0, .12, .031, DominantAxisGroup.None, ref count);
            Expect(filter, 0, .12, .060, DominantAxisGroup.None, ref count);
            Expect(filter, 0, .12, .062, DominantAxisGroup.Z, ref count);

            // One neutral report suffices when HID only sends the next changed sample.
            filter.Reset();
            Expect(filter, .5, .1, 0, DominantAxisGroup.Movement, ref count);
            Expect(filter, 0, 0, .020, DominantAxisGroup.Movement, ref count);
            Expect(filter, .1, .6, .061, DominantAxisGroup.None, ref count);
            Expect(filter, .1, .6, .092, DominantAxisGroup.Z, ref count);
            Expect(filter, 0, 0, .110, DominantAxisGroup.Z, ref count);
            Expect(filter, .6, .1, .151, DominantAxisGroup.Movement, ref count);

            // A stale lock or pending Z decision never crosses a disconnect or pause.
            filter.Reset();
            Expect(filter, .5, .1, 0, DominantAxisGroup.Movement, ref count);
            Expect(filter, 0, .9, .250, DominantAxisGroup.Movement, ref count);
            Expect(filter, 0, .9, .501, DominantAxisGroup.None, ref count);
            Expect(filter, 0, .9, .752, DominantAxisGroup.None, ref count);
            Expect(filter, 0, .9, .783, DominantAxisGroup.Z, ref count);
            Expect(filter, .9, 0, .780, DominantAxisGroup.None, ref count);
            Expect(filter, .9, 0, .781, DominantAxisGroup.Movement, ref count);

            // Invalid strengths, clocks, and timeouts clear any latched group.
            Expect(filter, double.NaN, .9, .790, DominantAxisGroup.None, ref count);
            Expect(filter, .9, 0, .791, DominantAxisGroup.Movement, ref count);
            Expect(filter, .9, -.1, .800, DominantAxisGroup.None, ref count);
            Expect(filter, .9, 0, .801, DominantAxisGroup.Movement, ref count);
            Expect(filter, .9, 0, double.PositiveInfinity, DominantAxisGroup.None, ref count);
            Expect(filter, .9, 0, .802, DominantAxisGroup.Movement, ref count);
            if (filter.Update(.9, 0, .810, 0) != DominantAxisGroup.None || filter.Group != DominantAxisGroup.None)
                throw new InvalidOperationException("Dominant axis filter retained a lock with an invalid timeout.");
            count++;

            // Reset also clears a pending decision that has never emitted output.
            Expect(filter, 0, .8, .820, DominantAxisGroup.None, ref count);
            filter.Reset();
            Expect(filter, 0, .8, .860, DominantAxisGroup.None, ref count);
            Expect(filter, 0, .8, .891, DominantAxisGroup.Z, ref count);

            return "PASS: " + count + " dominant axis filter checks (gesture lock and fresh input)\r\n";
        }

        private static void Expect(DominantAxisFilter filter, double movement, double z, double time, DominantAxisGroup expected, ref int count)
        {
            count++;
            DominantAxisGroup actual = filter.Update(movement, z, time, .25);
            if (actual != expected || filter.Group != expected)
                throw new InvalidOperationException("Dominant axis filter check " + count + " expected " + expected + " and got " + actual + ".");
        }
    }
}
