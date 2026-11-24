#! python 3
"""Toggle SpaceKat view navigation in Rhino 8 on Windows."""
import importlib.util
import math
from pathlib import Path

import Rhino
import scriptcontext
from Eto.Forms import UITimer
from Rhino.Geometry import Vector3d


TARGET_RHINO = 4
SESSION_KEY = "SpaceKat.Navigation.Rhino"


def load_bridge():
    path = Path(__file__).resolve().with_name("spacekat_bridge.py")
    if not path.is_file():
        raise FileNotFoundError("请解压完整的 Rhino 适配器 ZIP，并运行解压出的脚本。")
    spec = importlib.util.spec_from_file_location("spacekat_rhino_bridge", path)
    bridge = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(bridge)
    return bridge


def navigate(viewport, motion):
    pan_x, pan_y, zoom, orbit = motion
    target = viewport.CameraTarget
    if not target.IsValid:
        return False

    right, up = viewport.CameraX, viewport.CameraY
    changed = False
    if pan_x or pan_y:
        eye = viewport.CameraLocation
        valid_scale, pixels_per_unit = viewport.GetWorldToScreenScale(target)
        if valid_scale and pixels_per_unit > 0:
            span = viewport.Size.Height / pixels_per_unit
        else:
            span = eye.DistanceTo(target)
        if math.isfinite(span) and span > 0:
            shift = right * (pan_x * span) + up * (pan_y * span)
            target += shift
            viewport.SetCameraLocation(eye + shift, False)
            viewport.SetCameraTarget(target, False)
            changed = True

    if any(orbit) and not viewport.IsTwoPointPerspectiveProjection:
        back = Vector3d.CrossProduct(right, up)
        axis = right * orbit[0] + up * orbit[1] + back * orbit[2]
        angle = axis.Length
        if angle > 0 and axis.Unitize() and viewport.Rotate(angle, axis, target):
            changed = True

    if zoom != 1 and viewport.Magnify(1 / zoom, viewport.IsParallelProjection):
        changed = True
    return changed


class NavigationSession:
    def __init__(self, bridge):
        self.bridge = bridge
        self.clock = bridge.StepClock()
        self.timer = UITimer()
        self.timer.Interval = 1 / 30
        self.elapsed = self.on_elapsed
        self.closing = self.on_closing
        self.timer.Elapsed += self.elapsed

    def start(self):
        Rhino.RhinoApp.Closing += self.closing
        self.timer.Start()

    def stop(self):
        self.timer.Stop()
        self.timer.Elapsed -= self.elapsed
        Rhino.RhinoApp.Closing -= self.closing
        self.timer.Dispose()
        if scriptcontext.sticky.get(SESSION_KEY) is self:
            del scriptcontext.sticky[SESSION_KEY]

    def on_closing(self, _sender, _args):
        self.stop()

    def on_elapsed(self, _sender, _args):
        try:
            self.update_view()
        except Exception as error:
            self.stop()
            Rhino.RhinoApp.WriteLine("SpaceKat 导航已停止：" + str(error))

    def update_view(self):
        # Poll only while the main Rhino window owns foreground focus. The script
        # editor and layout views do not drive the model viewport.
        handle = Rhino.RhinoApp.MainWindowHandle()
        hwnd = handle.ToInt64() if hasattr(handle, "ToInt64") else int(handle)
        if self.bridge.foreground_window() != hwnd:
            self.clock.step(None)
            return
        axes = self.bridge.read(TARGET_RHINO)
        if not axes or not any(axes):
            self.clock.step(None)
            return

        doc = Rhino.RhinoDoc.ActiveDoc
        view = doc.Views.ActiveView if doc is not None else None
        if view is None or isinstance(view, Rhino.Display.RhinoPageView) or view.MouseCaptured(False):
            self.clock.step(None)
            return
        viewport = view.ActiveViewport
        if viewport is None or viewport.LockedProjection or not viewport.IsValidCamera or not viewport.IsValidFrustum:
            self.clock.step(None)
            return
        dt = self.clock.step(axes)
        if dt > 0 and navigate(viewport, self.bridge.motion(axes, dt)):
            view.Redraw()


def toggle_navigation():
    session = scriptcontext.sticky.get(SESSION_KEY)
    if session is not None:
        session.stop()
        Rhino.RhinoApp.WriteLine("SpaceKat 导航已停止。")
        return
    session = NavigationSession(load_bridge())
    scriptcontext.sticky[SESSION_KEY] = session
    try:
        session.start()
    except Exception:
        session.stop()
        raise
    Rhino.RhinoApp.WriteLine("SpaceKat 导航已启动。请回到 Rhino 模型视图。再次运行此脚本即可停止。")


if __name__ == "__main__":
    toggle_navigation()
