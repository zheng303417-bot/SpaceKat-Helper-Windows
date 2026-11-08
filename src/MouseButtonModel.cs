using System;
using System.Collections.Generic;

namespace SpaceKatMonitor
{
    public enum MouseButtonAction
    {
        None,
        LeftClick,
        RightClick,
        ToggleLeftHold,
        LeftDoubleClick,
        MiddleClick,
        BackClick,
        ForwardClick,
        ScrollUp,
        ScrollDown
    }

    public enum MouseButtonSignal
    {
        LeftDown,
        LeftUp,
        RightDown,
        RightUp,
        MiddleDown,
        MiddleUp,
        BackDown,
        BackUp,
        ForwardDown,
        ForwardUp,
        ScrollUp,
        ScrollDown
    }

    /// <summary>
    /// Converts fresh raw button press edges into mouse actions. Input silence
    /// does not change a latched left button; only its next press or an explicit
    /// release does. This class never sends desktop input.
    /// </summary>
    public sealed class MouseButtonModel
    {
        private int[] logicalBits = { 1, 2, 4, 5 };
        private MouseButtonAction[] actions = { MouseButtonAction.LeftClick,
            MouseButtonAction.RightClick, MouseButtonAction.ToggleLeftHold, MouseButtonAction.None };
        private uint previousMask;

        public bool Enabled { get; private set; }
        public bool IsLeftHeld { get; private set; }

        public void Configure(int[] bits, MouseButtonAction[] mappedActions)
        {
            if (Enabled)
                throw new InvalidOperationException("请先停止鼠标模拟，再修改按键映射。");
            if (bits == null || mappedActions == null || bits.Length != 4 || mappedActions.Length != 4)
                throw new ArgumentException("需要四个按键编号和四个鼠标动作。");
            for (int i = 0; i < 4; i++)
            {
                if (bits[i] < 0 || bits[i] > 31)
                    throw new ArgumentOutOfRangeException("bits", "按键位必须在 0 到 31 之间。");
                if (!Enum.IsDefined(typeof(MouseButtonAction), mappedActions[i]))
                    throw new ArgumentOutOfRangeException("mappedActions");
            }
            logicalBits = (int[])bits.Clone();
            actions = (MouseButtonAction[])mappedActions.Clone();
        }

        public void Enable(uint currentMask)
        {
            if (Enabled)
                throw new InvalidOperationException("鼠标按键模拟已经开启。");
            previousMask = currentMask;
            IsLeftHeld = false;
            Enabled = true;
        }

        public MouseButtonSignal[] Process(uint rawMask)
        {
            if (!Enabled) return new MouseButtonSignal[0];
            uint risingMask = rawMask & ~previousMask;
            previousMask = rawMask;
            uint assigned = 0;
            List<MouseButtonSignal> result = new List<MouseButtonSignal>();
            for (int i = 0; i < 4; i++)
            {
                uint bit = 1u << logicalBits[i];
                if (actions[i] == MouseButtonAction.None || (assigned & bit) != 0) continue;
                // A raw bit may appear on multiple cards. Execute its first
                // active mapping exactly once, including a toggle action.
                assigned |= bit;
                if ((risingMask & bit) == 0) continue;
                switch (actions[i])
                {
                    case MouseButtonAction.LeftClick:
                    case MouseButtonAction.LeftDoubleClick:
                        // Preserve the latched state while allowing an ordinary
                        // left click: finish the old hold, click, then re-hold.
                        if (IsLeftHeld) result.Add(MouseButtonSignal.LeftUp);
                        result.Add(MouseButtonSignal.LeftDown);
                        result.Add(MouseButtonSignal.LeftUp);
                        if (actions[i] == MouseButtonAction.LeftDoubleClick)
                        {
                            result.Add(MouseButtonSignal.LeftDown);
                            result.Add(MouseButtonSignal.LeftUp);
                        }
                        if (IsLeftHeld) result.Add(MouseButtonSignal.LeftDown);
                        break;
                    case MouseButtonAction.RightClick:
                        result.Add(MouseButtonSignal.RightDown);
                        result.Add(MouseButtonSignal.RightUp);
                        break;
                    case MouseButtonAction.ToggleLeftHold:
                        result.Add(IsLeftHeld ? MouseButtonSignal.LeftUp : MouseButtonSignal.LeftDown);
                        IsLeftHeld = !IsLeftHeld;
                        break;
                    case MouseButtonAction.MiddleClick:
                        result.Add(MouseButtonSignal.MiddleDown);
                        result.Add(MouseButtonSignal.MiddleUp);
                        break;
                    case MouseButtonAction.BackClick:
                        result.Add(MouseButtonSignal.BackDown);
                        result.Add(MouseButtonSignal.BackUp);
                        break;
                    case MouseButtonAction.ForwardClick:
                        result.Add(MouseButtonSignal.ForwardDown);
                        result.Add(MouseButtonSignal.ForwardUp);
                        break;
                    case MouseButtonAction.ScrollUp:
                        result.Add(MouseButtonSignal.ScrollUp);
                        break;
                    case MouseButtonAction.ScrollDown:
                        result.Add(MouseButtonSignal.ScrollDown);
                        break;
                }
            }
            return result.ToArray();
        }

        public MouseButtonSignal[] ReleaseLeftHold()
        {
            if (!IsLeftHeld) return new MouseButtonSignal[0];
            IsLeftHeld = false;
            return new[] { MouseButtonSignal.LeftUp };
        }

        public MouseButtonSignal[] Disable()
        {
            MouseButtonSignal[] release = ReleaseLeftHold();
            Enabled = false;
            previousMask = 0;
            return release;
        }
    }
}
