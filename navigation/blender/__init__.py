bl_info = {
    "name": "SpaceKat Navigation",
    "author": "SpaceKat Helper",
    "version": (1, 0, 0),
    "blender": (4, 2, 0),
    "location": "3D View > Sidebar > SpaceKat",
    "description": "Navigate the active 3D view from SpaceKat Helper on Windows",
    "category": "3D View",
}

import os
import time

import bpy
from bpy.app.handlers import persistent
from mathutils import Quaternion, Vector

from . import spacekat_bridge as bridge

_active = None


def move_view(view, axes, dt, clip_end):
    pan_x, pan_y, zoom, orbit = bridge.motion(axes, dt)
    rotation = view.view_rotation.copy()
    distance = view.view_distance
    if pan_x or pan_y:
        view.view_location += rotation @ Vector((pan_x * distance, pan_y * distance, 0))
    if zoom != 1:
        # Never jump to a limit when the user already has a closer or farther view.
        lo, hi = min(distance, 0.00001), max(distance, clip_end * 2)
        view.view_distance = min(hi, max(lo, max(distance, 0.00001) * zoom))
    delta = Vector(orbit)
    if delta.length > 0:
        view.view_rotation = (rotation @ Quaternion(delta.normalized(), delta.length)).normalized()


class VIEW3D_OT_spacekat_navigation(bpy.types.Operator):
    bl_idname = "view3d.spacekat_navigation"
    bl_label = "切换 SpaceKat 导航"

    def execute(self, context):
        global _active
        if _active is not None:
            _active.finish()
            return {'FINISHED'}
        if os.name != 'nt' or not context.area or context.area.type != 'VIEW_3D' or not context.window:
            return {'CANCELLED'}
        self._foreground_window = bridge.foreground_window()
        if self._foreground_window is None:
            return {'CANCELLED'}
        self._area = context.area.as_pointer()
        self._screen = context.screen.as_pointer()
        self._window = context.window.as_pointer()
        self._wm = context.window_manager
        self._clock = bridge.StepClock()
        self._polled = 0
        self._owns_motion = False
        self._stopped = False
        self._timer = self._wm.event_timer_add(1 / 60, window=context.window)
        self._wm.modal_handler_add(self)
        _active = self
        context.area.tag_redraw()
        return {'RUNNING_MODAL'}

    def modal(self, context, event):
        if self._stopped:
            return {'CANCELLED'}
        if not context.window or context.window.as_pointer() != self._window:
            return {'PASS_THROUGH'}
        if not context.screen or context.screen.as_pointer() != self._screen:
            self.finish()
            return {'CANCELLED'}
        if event.type == 'NDOF_MOTION' and self._owns_motion and context.area and context.area.as_pointer() == self._area:
            return {'RUNNING_MODAL'}
        if event.type != 'TIMER':
            return {'PASS_THROUGH'}
        now = time.monotonic()
        if now - self._polled < 0.012:
            return {'PASS_THROUGH'}
        self._polled = now
        area = next((area for area in context.screen.areas if area.as_pointer() == self._area), None)
        if area is None or area.type != 'VIEW_3D':
            self.finish()
            return {'CANCELLED'}
        space = area.spaces.active
        view = space.region_3d
        if view is None or view.view_perspective == 'CAMERA' or len(space.region_quadviews) or space.lock_object or space.lock_cursor:
            self._clock.step(None)
            self._owns_motion = False
            return {'PASS_THROUGH'}
        axes = bridge.read(bridge.BLENDER) if bridge.foreground_window() == self._foreground_window else None
        self._owns_motion = axes is not None
        dt = self._clock.step(axes)
        if dt > 0:
            move_view(view, axes, dt, space.clip_end)
            area.tag_redraw()
        return {'PASS_THROUGH'}

    def finish(self):
        global _active
        if self._stopped:
            return
        self._stopped = True
        self._wm.event_timer_remove(self._timer)
        if _active is self:
            _active = None
        for window in self._wm.windows:
            for area in window.screen.areas:
                if area.type == 'VIEW_3D':
                    area.tag_redraw()

    def cancel(self, context):
        self.finish()


class VIEW3D_PT_spacekat_navigation(bpy.types.Panel):
    bl_label = "SpaceKat 六轴导航"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "SpaceKat"

    def draw(self, context):
        if os.name != 'nt':
            self.layout.label(text="此适配器仅支持 Windows")
            return
        self.layout.operator(VIEW3D_OT_spacekat_navigation.bl_idname,
                             text="停止导航" if _active is not None else "启动导航")
        self.layout.label(text="同时在小助手里开启 3D 导航")


@persistent
def _before_load(_):
    if _active is not None:
        _active.finish()


def register():
    bpy.utils.register_class(VIEW3D_OT_spacekat_navigation)
    bpy.utils.register_class(VIEW3D_PT_spacekat_navigation)
    bpy.app.handlers.load_pre.append(_before_load)


def unregister():
    _before_load(None)
    bpy.app.handlers.load_pre.remove(_before_load)
    bpy.utils.unregister_class(VIEW3D_PT_spacekat_navigation)
    bpy.utils.unregister_class(VIEW3D_OT_spacekat_navigation)
