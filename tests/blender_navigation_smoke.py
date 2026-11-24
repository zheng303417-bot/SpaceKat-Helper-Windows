"""Run with Blender in background mode via tools/Test-BlenderNavigation.ps1."""
import sys
import time
from types import SimpleNamespace

import addon_utils
import bpy
from mathutils import Quaternion, Vector


addon_directory, mapping_name = sys.argv[sys.argv.index('--') + 1:]
sys.path.insert(0, addon_directory)
addon = addon_utils.enable('spacekat_navigation', default_set=False)
assert addon is not None and addon_utils.check('spacekat_navigation')[1], 'Packaged add-on failed to load'
assert hasattr(bpy.types, 'VIEW3D_PT_spacekat_navigation')
try:
    bridge = addon.bridge
    axes = None
    deadline = time.monotonic() + 1
    while axes is None and time.monotonic() < deadline:
        axes = bridge.read(bridge.BLENDER, name=mapping_name, require_foreground=False)
        if axes is None:
            time.sleep(0.01)
    assert axes and all(axes), 'C# publisher did not deliver six live axes'
    assert bridge.read(bridge.FUSION, name=mapping_name, require_foreground=False) is None, 'Wrong target was accepted'
    assert bridge.read(bridge.BLENDER, name=mapping_name) is None, 'Background application was not blocked'
    area = next(a for a in bpy.context.screen.areas if a.type == 'VIEW_3D')
    view = area.spaces.active.region_3d
    view.view_perspective = 'PERSP'
    view.view_location = Vector((0, 0, 0))
    view.view_rotation = Quaternion()
    view.view_distance = 10
    cube = bpy.context.scene.objects['Cube']
    original = cube.matrix_world.copy()
    # Blender runs headless for this test. Override only the focus signal and
    # mapping name so we can drive the real operator and real viewport.
    read = bridge.read
    bridge.read = lambda target: read(target, name=mapping_name, require_foreground=False)
    bridge.foreground_window = lambda: 1
    region = next(r for r in area.regions if r.type == 'WINDOW')
    with bpy.context.temp_override(area=area, region=region):
        assert bpy.ops.view3d.spacekat_navigation() == {'RUNNING_MODAL'}, 'Operator did not start'
        operator = addon._active
        for _ in range(5):
            operator.modal(bpy.context, SimpleNamespace(type='TIMER'))
            time.sleep(0.017)
        assert view.view_location.x < 0 and view.view_location.y > 0, 'Two simultaneous pan axes did not move the real viewport'
        assert 8 < view.view_distance < 10, 'Z did not zoom the real viewport'
        assert all(abs(component) > 0.005 for component in view.view_rotation[1:]), 'Three rotation axes did not reach the real viewport'
        assert cube.matrix_world == original, 'Navigation changed the model instead of the view'
        distance = view.view_distance
        bridge.foreground_window = lambda: 2
        operator.modal(bpy.context, SimpleNamespace(type='TIMER'))
        assert view.view_distance == distance and not operator._owns_motion, 'Focus change did not pause output'
        assert bpy.ops.view3d.spacekat_navigation() == {'FINISHED'} and addon._active is None, 'Operator did not stop'
finally:
    addon_utils.disable('spacekat_navigation', default_set=False)
print('PASS Blender', bpy.app.version_string, 'compiled helper -> packaged add-on -> live viewport; foreground and target guards')
