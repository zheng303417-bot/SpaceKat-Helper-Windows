using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Media3D;
using Microsoft.Win32.SafeHandles;

namespace SpaceKatMonitor
{
    internal sealed class SolidWorksNavigationOutput : IDisposable
    {
        private const string Waiting = "请切到 SOLIDWORKS 的零件或装配体视图";
        private const int MotionIntervalMs = 16;
        private const int ViewCheckIntervalMs = 100;
        private const int ViewCheckMaxAgeMs = 200;
        private const uint CreateHighResolutionTimer = 0x2;
        private const uint TimerSynchronizeAndModify = 0x100002;
        private sealed class Sample
        {
            internal readonly int Session;
            internal readonly long Time;
            internal readonly float[] Axes;
            internal Sample(int session, long time, float[] axes)
            {
                Session = session;
                Time = time;
                Axes = (float[])axes.Clone();
            }
            internal bool Moving
            {
                get
                {
                    long age = NavigationBridge.GetTickCount64() - Time;
                    if (Time <= 0 || age < 0 || age >= NavigationBridge.SampleTimeoutMs) return false;
                    foreach (float axis in Axes) if (axis != 0) return true;
                    return false;
                }
            }
        }

        private volatile Sample latest;
        private volatile string status = Waiting;
        private volatile bool disposed;
        private int session;
        private Thread worker;
        internal string Status { get { return status; } }

        internal static void ApplyPrecisionCurve(float[] axes, double linear, double angular)
        {
            SoftenNearCenter(axes, 0, linear);
            SoftenNearCenter(axes, 3, angular);
        }

        private static void SoftenNearCenter(float[] axes, int first, double gain)
        {
            if (gain <= 0) return;
            double magnitude = Math.Sqrt(axes[first] * axes[first] + axes[first + 1] * axes[first + 1] +
                axes[first + 2] * axes[first + 2]);
            float weight = (float)(0.45 + 0.55 * Math.Min(1, magnitude / gain));
            for (int i = first; i < first + 3; i++) axes[i] *= weight;
        }

        internal void Publish(long sampleTime, float[] axes)
        {
            latest = new Sample(session, sampleTime, axes);
            if (worker != null) return;
            worker = new Thread(Poll) { IsBackground = true, Name = "SpaceKat SOLIDWORKS" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        internal void Stop()
        {
            latest = null;
            session++;
            status = Waiting;
        }

        public void Dispose()
        {
            Stop();
            disposed = true;
            if (worker != null) worker.Join(400);
        }

        private void Poll()
        {
            SolidWorksViewApi app = null;
            var target = new ViewTarget();
            IntPtr frame = IntPtr.Zero, foreground = IntPtr.Zero, focus = IntPtr.Zero;
            int processId = 0, inputSession = -1;
            long retryAfter = 0, nextViewCheck = 0;
            var clock = Stopwatch.StartNew();
            using (SafeWaitHandle timer = CreateWaitableTimerEx(IntPtr.Zero, null, CreateHighResolutionTimer, TimerSynchronizeAndModify))
            try
            {
                while (!disposed)
                {
                    long startedAt = clock.ElapsedTicks;
                    long started = clock.ElapsedMilliseconds;
                    Sample sample = latest;
                    IntPtr window = sample == null ? IntPtr.Zero : GetForegroundWindow();
                    try
                    {
                        if (window != foreground)
                        {
                            Disconnect(ref app, target);
                            foreground = window;
                            processId = SolidWorksProcess(window);
                            retryAfter = nextViewCheck = 0;
                        }
                        IntPtr currentFocus;
                        if (sample == null || processId == 0 || !FrameAvailable(window, out currentFocus))
                        {
                            target.Clear();
                            nextViewCheck = 0;
                            status = Waiting;
                        }
                        else if (started >= retryAfter)
                        {
                            if (inputSession != sample.Session || focus != currentFocus)
                            {
                                target.StopMotion();
                                inputSession = sample.Session;
                                focus = currentFocus;
                                nextViewCheck = 0;
                            }
                            if (app == null)
                            {
                                app = SolidWorksViewApi.Connect(processId, out frame);
                                if (app == null)
                                {
                                    status = "等待当前 SOLIDWORKS 实例。请确认模型窗口已打开";
                                    retryAfter = started + 1000;
                                }
                            }
                            if (app != null && frame == window)
                            {
                                bool moving = sample.Moving;
                                if (!moving) target.StopMotion();
                                if (started >= nextViewCheck || (moving && target.View != null && target.LastStep == 0))
                                {
                                    RefreshView(app, window, target);
                                    nextViewCheck = clock.ElapsedMilliseconds + (moving ? ViewCheckIntervalMs : 150);
                                }
                                if (target.View != null && moving)
                                    MoveView(app, window, target, sample.Session, clock.ElapsedTicks);
                            }
                            else if (app != null) status = Waiting;
                        }
                    }
                    catch (Exception error)
                    {
                        Exception cause = error.GetBaseException();
                        if (!IsConnectionError(cause)) throw;
                        Disconnect(ref app, target);
                        retryAfter = clock.ElapsedMilliseconds + 1000;
                        nextViewCheck = 0;
                        if (cause is FileNotFoundException || cause is FileLoadException || cause is BadImageFormatException ||
                            cause is TypeLoadException || cause is MissingMemberException)
                            status = "无法加载当前 SOLIDWORKS 安装目录中的视图接口";
                        else if (cause is Win32Exception || cause is UnauthorizedAccessException)
                            status = "无法读取当前 SOLIDWORKS 进程。请确认两个程序以相同权限运行";
                        else
                            status = "SOLIDWORKS 视图调用失败，正在重试 (0x" + cause.HResult.ToString("X8") + ")";
                    }
                    if (sample == null) Thread.Sleep(125);
                    else
                    {
                        long remaining = startedAt + Stopwatch.Frequency * MotionIntervalMs / 1000 - clock.ElapsedTicks;
                        if (remaining > 0)
                        {
                            long due = -Math.Max(1, remaining * 10000000 / Stopwatch.Frequency);
                            if (!timer.IsInvalid && SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                                WaitForSingleObject(timer, 2 * MotionIntervalMs);
                            else Thread.Sleep((int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency));
                        }
                    }
                }
            }
            finally { Disconnect(ref app, target); }
        }

        private static bool IsConnectionError(Exception cause)
        {
            return cause is COMException || cause is InvalidComObjectException || cause is InvalidCastException ||
                cause is ArgumentException || cause is InvalidOperationException || cause is IOException ||
                cause is BadImageFormatException || cause is TypeLoadException || cause is MissingMemberException ||
                cause is Win32Exception || cause is UnauthorizedAccessException;
        }

        private static void Disconnect(ref SolidWorksViewApi app, ViewTarget target)
        {
            try { target.Clear(); }
            catch (Exception error) { if (!IsConnectionError(error.GetBaseException())) throw; }
            finally
            {
                if (app != null) app.Dispose();
                app = null;
            }
        }

        private void RefreshView(SolidWorksViewApi app, IntPtr window, ViewTarget target)
        {
            object document = app.ActiveDocument();
            object view = null;
            try
            {
                int type = document == null ? 0 : app.DocumentType(document);
                if (type != 1 && type != 2)
                {
                    target.Clear();
                    status = "等待 SOLIDWORKS 零件或装配体，工程图不参与导航";
                    return;
                }

                if (app.IsCommandActive() || app.IsEditingSketch(document) || app.IsViewOnly(document))
                {
                    target.Clear();
                    status = "SOLIDWORKS 正在执行命令、编辑草图或载入视图，导航已暂停";
                    return;
                }

                view = app.ActiveView(document);
                IntPtr handle = view == null ? IntPtr.Zero : app.ViewHandle(view);
                if (handle == IntPtr.Zero || !IsChild(window, handle))
                {
                    target.Clear();
                    status = Waiting;
                    return;
                }
                if (target.Handle != handle || !ReferenceEquals(target.Document, document))
                {
                    target.Clear();
                    target.Document = document;
                    target.View = view;
                    target.Handle = handle;
                    target.DocumentType = type;
                    document = null;
                    view = null;
                }
                else if (!ReferenceEquals(target.View, view))
                {
                    // Some releases return a new COM wrapper for the same view window.
                    target.StopMotion();
                    SolidWorksViewApi.Release(target.View);
                    target.View = view;
                    view = null;
                }
                target.CheckedAt = Stopwatch.GetTimestamp();
                status = "已连接 SOLIDWORKS · 六轴视图导航";
            }
            finally
            {
                SolidWorksViewApi.Release(view);
                SolidWorksViewApi.Release(document);
            }
        }

        private void MoveView(SolidWorksViewApi app, IntPtr frame, ViewTarget target, int inputSession, long now)
        {
            Sample sample = latest;
            if (sample == null || sample.Session != inputSession || !sample.Moving ||
                !IsWindowVisible(target.Handle) || !IsChild(frame, target.Handle) || GetForegroundWindow() != frame ||
                Stopwatch.GetTimestamp() - target.CheckedAt > Stopwatch.Frequency * ViewCheckMaxAgeMs / 1000)
            {
                target.StopMotion();
                return;
            }

            long previous = target.LastStep;
            target.LastStep = now;
            // Apply a bounded first frame as well, including a brief single-axis push.
            double dt = previous == 0 ? MotionIntervalMs / 1000.0 :
                Math.Min(.05, (now - previous) / (double)Stopwatch.Frequency);
            ApplyMotion(app, target, sample, frame, dt);
        }

        private void ApplyMotion(SolidWorksViewApi app, ViewTarget target, Sample sample, IntPtr frame, double dt)
        {
            object view = target.View;
            Rect rect;
            if (!CanMove(frame, sample) || !GetClientRect(target.Handle, out rect) || rect.Right <= 0 || rect.Bottom <= 0)
                return;
            Sample current = latest;
            if (current == null || current.Session != sample.Session || !CanMove(frame, current)) return;

            float[] a = current.Axes;
            if (a[2] != 0 && a[0] == 0 && a[1] == 0 && a[3] == 0 && a[4] == 0 && a[5] == 0)
            {
                // The native zoom applies scale and translation together. Re-read the full pose when another axis joins.
                double scale = target.Pose != null ? target.Pose.Scale : target.ZoomOnlyScale ?? app.ViewScale(view);
                if (target.Pose != null) target.ReleasePose();
                target.ZoomOnlyScale = scale;
                double factor = Math.Exp(1.8 * a[2] * dt);
                if (factor != 1 && ZoomInRange(scale * factor) && CanMove(frame, current))
                {
                    // ZoomByFactor also refreshes the view.
                    app.Zoom(view, factor);
                    target.ZoomOnlyScale = scale * factor;
                }
                return;
            }
            target.ZoomOnlyScale = null;

            if (target.Pose == null || target.Width != rect.Right || target.Height != rect.Bottom)
            {
                target.ReleasePose();
                target.Pose = app.ReadPose(view);
                target.Width = rect.Right;
                target.Height = rect.Bottom;
            }
            var pose = target.Pose;
            current = latest;
            if (pose == null || current == null || current.Session != sample.Session || !CanMove(frame, current)) return;

            a = current.Axes;
            bool rotate = a[3] != 0 || a[4] != 0 || a[5] != 0;
            if (rotate && target.RotationCenter == null)
            {
                target.RotationCenter = pose.RotationCenter(app.ModelBounds(target.Document, target.DocumentType),
                    rect.Right, rect.Bottom);
                // The first bounds query can take longer on a large model. Use the input
                // that is current when the view is ready, including a release during the query.
                current = latest;
                if (current == null || current.Session != sample.Session || !CanMove(frame, current)) return;
                a = current.Axes;
                rotate = a[3] != 0 || a[4] != 0 || a[5] != 0;
            }

            double pan = rect.Bottom / (pose.ViewUnitsToPixels * pose.Scale) * dt;
            double dx = a[0] * pan, dy = -a[1] * pan;
            var pivot = target.RotationCenter.GetValueOrDefault();
            double zoom = Math.Exp(1.8 * a[2] * dt);
            bool zoomed = !pose.Perspective && zoom != 1 && ZoomInRange(pose.Scale * zoom);
            if (zoomed)
            {
                if (!CanMove(frame, current)) return;
                double previousScale = pose.Scale;
                var previousOffset = new Vector3D(pose.Offset[0], pose.Offset[1], pose.Offset[2]);
                app.Zoom(view, zoom);
                if (!app.ReadZoomedPose(view, pose))
                {
                    target.ReleasePose();
                    if (GetForegroundWindow() == frame) app.Redraw(target.Document);
                    return;
                }
                // Transform the active orbit center by the actual zoom, including SOLIDWORKS' translation.
                double ratio = pose.Scale / previousScale;
                var offsetAfterZoom = new Vector3D(pose.Offset[0], pose.Offset[1], pose.Offset[2]);
                pivot = (pivot - previousOffset) * ratio + offsetAfterZoom;
                dx *= ratio;
                dy *= ratio;
            }
            pose.Pan(dx, dy);
            pivot += new Vector3D(dx, dy, 0);
            target.RotationCenter = rotate ? (Vector3D?)pivot : null;

            bool shifted = dx != 0 || dy != 0;
            Func<bool> canMove = delegate { return CanMove(frame, current); };
            bool applied = rotate
                ? app.WriteRotation(view, pose, new Vector3D(a[3], a[4], a[5]) * (1.8 * dt), pivot, canMove)
                : !shifted || app.WritePose(view, pose, false, canMove);
            if (!applied)
            {
                target.ReleasePose();
                return;
            }
            bool changed = rotate || shifted || zoomed;
            if (pose.Perspective && zoom != 1 && ZoomInRange(pose.Scale * zoom) && CanMove(frame, current))
            {
                app.Zoom(view, zoom);
                target.ReleasePose();
                changed = true;
            }
            if (changed && GetForegroundWindow() == frame) app.Redraw(target.Document);
        }

        private static bool ZoomInRange(double scale) { return scale >= 1e-9 && scale <= 1e9; }

        private bool CanMove(IntPtr frame, Sample sample)
        {
            Sample current = latest;
            return current != null && current.Session == sample.Session && sample.Moving && current.Moving &&
                GetForegroundWindow() == frame;
        }

        private static bool FrameAvailable(IntPtr window, out IntPtr focus)
        {
            focus = IntPtr.Zero;
            uint pid;
            uint thread = GetWindowThreadProcessId(window, out pid);
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf(typeof(GuiThreadInfo)) };
            if (thread == 0 || !IsWindowEnabled(window) || GetWindow(window, 4) != IntPtr.Zero ||
                !GetGUIThreadInfo(thread, ref info) || (info.Flags & 0x1E) != 0 || info.Capture != IntPtr.Zero)
                return false;
            focus = info.Focus;
            return true;
        }

        // COM objects live on the output thread and are released on a view change or shutdown.
        private sealed class ViewTarget
        {
            internal object Document, View;
            internal IntPtr Handle;
            internal long LastStep, CheckedAt;
            internal int DocumentType, Width, Height;
            internal SolidWorksViewApi.ViewPose Pose;
            internal Vector3D? RotationCenter;
            internal double? ZoomOnlyScale;

            internal void ReleasePose()
            {
                if (Pose != null) Pose.Dispose();
                Pose = null;
                RotationCenter = null;
                ZoomOnlyScale = null;
            }

            internal void StopMotion()
            {
                LastStep = 0;
                ReleasePose();
            }

            internal void Clear()
            {
                StopMotion();
                SolidWorksViewApi.Release(View);
                SolidWorksViewApi.Release(Document);
                View = Document = null;
                Handle = IntPtr.Zero;
                LastStep = CheckedAt = 0;
            }
        }

        private static int SolidWorksProcess(IntPtr window)
        {
            uint pid;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out pid) == 0) return 0;
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                    return String.Equals(process.ProcessName, "SLDWORKS", StringComparison.OrdinalIgnoreCase) ? (int)pid : 0;
            }
            catch (ArgumentException) { return 0; }
            catch (InvalidOperationException) { return 0; }
            catch (System.ComponentModel.Win32Exception) { return 0; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretRect;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll")]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, IntPtr completion,
            IntPtr argument, bool resume);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    }
}
