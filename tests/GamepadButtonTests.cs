using System;

namespace SpaceKatMonitor
{
    internal static class GamepadButtonTests
    {
        public static string Run()
        {
            int count = 0;
            GamepadButtons[] actions;
            Check(GamepadButtonModel.TryParse("Y,None,A,X", out actions)
                && actions[0] == GamepadButtons.Y && actions[1] == GamepadButtons.None
                && actions[2] == GamepadButtons.A && actions[3] == GamepadButtons.X, ref count);
            Check(!GamepadButtonModel.TryParse("A,B,X", out actions)
                && !GamepadButtonModel.TryParse("A,B,X,Start", out actions), ref count);
            int[] bits = { 1, 2, 4, 5 };
            GamepadButtons[] defaults = { GamepadButtons.A, GamepadButtons.B, GamepadButtons.X, GamepadButtons.Y };
            GamepadButtonModel model = new GamepadButtonModel();
            model.Begin(bits, defaults, -1, 2);
            model.Observe(2, 1.0);
            Check(model.Output == GamepadButtons.None, ref count);
            model.Observe(0, 1.003);
            model.Observe(2, 1.01);
            Check(model.Output == GamepadButtons.A, ref count);
            model.Observe(0, 1.015);
            model.Step(1.04);
            Check(model.Output == GamepadButtons.A, ref count);
            model.Step(1.051);
            Check(model.Output == GamepadButtons.None, ref count);
            model.Observe(0x36, 2.0);
            model.Step(3.0);
            Check((ushort)model.Output == 0xF000, ref count);
            model.Observe(0x10, 3.0);
            Check(model.Output == GamepadButtons.X, ref count);
            model.Reset();
            model.Step(3.01);
            Check(model.Output == GamepadButtons.None, ref count);

            model.Begin(new[] { 1, 1, 4, 5 }, defaults, 4, 0);
            model.Observe(0x36, 4.0);
            Check(model.Output == (GamepadButtons.A | GamepadButtons.Y), ref count);
            model.Begin(bits, new[] { GamepadButtons.B, GamepadButtons.B, GamepadButtons.None, GamepadButtons.X }, -1, 0);
            model.Observe(6, 5.0);
            model.Observe(4, 5.1);
            Check(model.Output == GamepadButtons.B, ref count);
            model.Observe(0x10, 5.2);
            Check(model.Output == GamepadButtons.None, ref count);
            return "PASS: " + count + " Xbox face-button mapping checks (short pulses, holds, routing and release)\r\n";
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Xbox face-button assertion " + count + " failed.");
        }
    }
}
