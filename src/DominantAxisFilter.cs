namespace SpaceKatMonitor
{
    internal enum DominantAxisGroup { None, Movement, Z }

    internal sealed class DominantAxisFilter
    {
        private const double MovementEntry = .08;
        private const double ZEntry = .12;
        private const double ZAdvantage = 1.25;
        private const double NeutralStrength = .065;
        private const double ZConfirmationSeconds = .030;
        private const double NeutralConfirmationSeconds = .040;

        private double lastSample = double.NaN;
        private double zDominantSince = double.NaN;
        private double neutralSince = double.NaN;

        public DominantAxisGroup Group { get; private set; }

        public DominantAxisGroup Update(double movementStrength, double zStrength, double nowSeconds, double sampleTimeoutSeconds)
        {
            if (!IsFiniteNonnegative(movementStrength) || !IsFiniteNonnegative(zStrength) ||
                !IsFiniteNonnegative(nowSeconds) || !IsFiniteNonnegative(sampleTimeoutSeconds) ||
                sampleTimeoutSeconds == 0 || nowSeconds < lastSample)
            {
                Reset();
                return Group;
            }

            if (nowSeconds - lastSample > sampleTimeoutSeconds) Reset();
            lastSample = nowSeconds;

            // The previous neutral report remains current until this sample arrives.
            if (nowSeconds - neutralSince >= NeutralConfirmationSeconds)
            {
                Group = DominantAxisGroup.None;
                neutralSince = double.NaN;
            }

            if (Group != DominantAxisGroup.None)
            {
                if (movementStrength > NeutralStrength || zStrength > NeutralStrength)
                    neutralSince = double.NaN;
                else if (double.IsNaN(neutralSince))
                    neutralSince = nowSeconds;
                return Group;
            }

            // A brief Z lead at the start of a sideways gesture should not lock out movement.
            if (zStrength >= ZEntry && zStrength >= movementStrength * ZAdvantage)
            {
                if (double.IsNaN(zDominantSince))
                    zDominantSince = nowSeconds;
                else if (nowSeconds - zDominantSince >= ZConfirmationSeconds)
                {
                    Group = DominantAxisGroup.Z;
                    zDominantSince = double.NaN;
                }
                return Group;
            }

            zDominantSince = double.NaN;
            if (movementStrength >= MovementEntry) Group = DominantAxisGroup.Movement;
            return Group;
        }

        public void Reset()
        {
            Group = DominantAxisGroup.None;
            lastSample = double.NaN;
            zDominantSince = double.NaN;
            neutralSince = double.NaN;
        }

        private static bool IsFiniteNonnegative(double value)
        {
            return value >= 0 && !double.IsInfinity(value);
        }
    }
}
