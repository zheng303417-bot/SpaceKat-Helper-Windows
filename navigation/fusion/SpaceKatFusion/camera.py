"""Fusion viewport camera navigation. Call apply_view on Fusion's main thread."""
import math

import adsk.core


def _add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def _sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def _scale(a, s):
    return tuple(x * s for x in a)


def _dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def _cross(a, b):
    return (a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0])


def _unit(a):
    length = math.sqrt(_dot(a, a))
    if not math.isfinite(length) or length < 1e-12:
        raise ValueError('Invalid Fusion camera direction')
    return _scale(a, 1.0 / length)


def _xyz(v):
    return (v.x, v.y, v.z)


def _rotate(v, axis, angle):
    c, s = math.cos(angle), math.sin(angle)
    return _add(_add(_scale(v, c), _scale(_cross(axis, v), s)),
                _scale(axis, _dot(axis, v) * (1 - c)))


def _point(v):
    return adsk.core.Point3D.create(*v)


def _vector(v):
    return adsk.core.Vector3D.create(*v)


def _extents(camera):
    # getExtents/setExtents superseded viewExtents in October 2023.
    if hasattr(camera, 'getExtents'):
        ok, width, height = camera.getExtents()
        if ok and math.isfinite(width) and math.isfinite(height) and width > 0 and height > 0:
            return width, height
        raise ValueError('Invalid orthographic camera extents')
    radius = camera.viewExtents
    if not math.isfinite(radius) or radius <= 0:
        raise ValueError('Invalid orthographic camera extent')
    return 2 * radius, 2 * radius


def _set_extents(camera, width, height):
    if hasattr(camera, 'setExtents'):
        if not camera.setExtents(width, height):
            raise RuntimeError('Fusion rejected camera extents')
    else:
        camera.viewExtents = height / 2


def apply_view(viewport, axes, dt, commands):
    """Apply ordered commands and one incremental six-axis step; return changed.

    axes are X, Y, Z, Rx, Ry, Rz, shaped in [-3, 3]. Positive X/Y
    move the model right/up; positive Z zooms in. Commands: 2 fit,
    3 top, 5 right, 6 front. Camera target is the rotation center.
    """
    changed = False
    orientations = {3: adsk.core.ViewOrientations.TopViewOrientation,
                    5: adsk.core.ViewOrientations.RightViewOrientation,
                    6: adsk.core.ViewOrientations.FrontViewOrientation}
    for command in commands:
        if command == 2 or command in orientations:
            camera = viewport.camera
            camera.isSmoothTransition = False
            if command == 2:
                camera.isFitView = True
            else:
                camera.viewOrientation = orientations[command]
            viewport.camera = camera
            changed = True

    if len(axes) != 6 or not math.isfinite(dt) or dt <= 0:
        return changed
    if any(not math.isfinite(v) or abs(v) > 3 for v in axes):
        return changed
    dt = min(dt, 0.04)
    if not any(axes):
        return changed

    x, y, z, rx, ry, rz = axes
    camera = viewport.camera
    eye, target, up = _xyz(camera.eye), _xyz(camera.target), _xyz(camera.upVector)
    back_offset = _sub(eye, target)
    distance = math.sqrt(_dot(back_offset, back_offset))
    back = _unit(back_offset)
    right = _unit(_cross(up, back))
    up = _unit(_cross(back, right))

    orthographic = camera.cameraType == adsk.core.CameraTypes.OrthographicCameraType
    if orthographic:
        width, height = _extents(camera)
        span = height
    else:
        # Field of view is in radians. Height at the target gives pan scale.
        span = 2 * distance * math.tan(camera.perspectiveAngle / 2)
        if not math.isfinite(span) or span <= 0:
            span = distance

    shift = _add(_scale(right, -x * dt * span), _scale(up, -y * dt * span))
    target = _add(target, shift)
    eye = _add(eye, shift)

    # Axis-angle rotation in camera coordinates preserves a valid up vector
    # through poles, allowing unrestricted pitch and roll.
    rotation = _add(_add(_scale(right, rx), _scale(up, ry)), _scale(back, rz))
    amount = math.sqrt(_dot(rotation, rotation))
    if amount > 0:
        axis = _scale(rotation, 1 / amount)
        angle = amount * dt * 1.8
        back_offset = _rotate(back_offset, axis, angle)
        up = _rotate(up, axis, angle)

    factor = math.exp(-1.8 * z * dt)
    if orthographic:
        if z:
            _set_extents(camera, max(1e-9, width * factor), max(1e-9, height * factor))
    else:
        back_offset = _scale(back_offset, max(1e-9, distance * factor) / distance)

    eye = _add(target, back_offset)
    camera.eye = _point(eye)
    camera.target = _point(target)
    camera.upVector = _vector(_unit(up))
    camera.isSmoothTransition = False
    camera.isFitView = False
    viewport.camera = camera
    return True
