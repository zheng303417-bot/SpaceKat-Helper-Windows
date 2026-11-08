using System;

namespace SpaceKatMonitor
{
    [Flags]
    internal enum GamepadButtons : ushort
    {
        None = 0,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }

    internal sealed class GamepadButtonModel
    {
        internal static readonly GamepadButtons[] Options =
            { GamepadButtons.None, GamepadButtons.A, GamepadButtons.B, GamepadButtons.X, GamepadButtons.Y };
        internal const GamepadButtons FaceButtons = GamepadButtons.A | GamepadButtons.B | GamepadButtons.X | GamepadButtons.Y;
        private const double MinimumPressSeconds = 0.04;
        private readonly uint[] bindings = new uint[4];
        private readonly double[] releaseAt = new double[4];
        private GamepadButtons pressed;
        private uint heldAtStart;

        public GamepadButtons Output { get; private set; }

        public void Begin(int[] bits, GamepadButtons[] actions, int toggleBit, uint currentMask)
        {
            Reset();
            heldAtStart = currentMask;
            uint assigned = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                int action = Array.IndexOf(Options, actions[i]);
                int bit = bits[i];
                if (action <= 0 || bit < 0 || bit > 31 || bit == toggleBit) continue;
                uint mask = 1u << bit;
                if ((assigned & mask) != 0) continue;
                assigned |= mask;
                bindings[action - 1] |= mask;
            }
        }

        public void Observe(uint mask, double now)
        {
            heldAtStart &= mask;
            mask &= ~heldAtStart;
            GamepadButtons next = GamepadButtons.None;
            for (int i = 0; i < bindings.Length; i++)
            {
                if ((mask & bindings[i]) == 0) continue;
                GamepadButtons button = Options[i + 1];
                next |= button;
                // SpaceKat can send a press and release only a few milliseconds apart.
                if ((pressed & button) == 0) releaseAt[i] = now + MinimumPressSeconds;
            }
            pressed = next;
            Step(now);
        }

        public void Step(double now)
        {
            GamepadButtons output = pressed;
            for (int i = 0; i < releaseAt.Length; i++)
                if (now < releaseAt[i]) output |= Options[i + 1];
            Output = output;
        }

        public void Reset()
        {
            Array.Clear(bindings, 0, bindings.Length);
            Array.Clear(releaseAt, 0, releaseAt.Length);
            heldAtStart = 0;
            pressed = Output = GamepadButtons.None;
        }

        internal static bool TryParse(string text, out GamepadButtons[] actions)
        {
            actions = null;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] fields = text.Trim().Split(',');
            if (fields.Length != 4) return false;
            GamepadButtons[] parsed = new GamepadButtons[4];
            for (int i = 0; i < parsed.Length; i++)
                if (!Enum.TryParse(fields[i].Trim(), true, out parsed[i]) ||
                    Array.IndexOf(Options, parsed[i]) < 0) return false;
            actions = parsed;
            return true;
        }
    }
}
