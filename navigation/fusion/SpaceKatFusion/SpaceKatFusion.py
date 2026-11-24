"""Fusion main-thread dispatcher for the local SpaceKat bridge."""
import ctypes
from ctypes import wintypes
import math
import os
import struct
import threading
import time
import traceback

import adsk.core
from .camera import apply_view

_MAP_NAME = r'Local\SpaceKat.FusionCamera.Input.v1'
_READY_NAME = r'Local\SpaceKat.FusionCamera.Ready.v1'
_EVENT_ID = 'SpaceKat.FusionCamera.Input.v1'
_MAP_SIZE = 824
_MAGIC = 0x32464B53
_CAPACITY = 32
_MAX_AGE_MS = 150
_COMMANDS = (2, 3, 5, 6)

_kernel = ctypes.WinDLL('kernel32', use_last_error=True)
_user = ctypes.WinDLL('user32', use_last_error=True)
_kernel.OpenFileMappingW.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR)
_kernel.OpenFileMappingW.restype = wintypes.HANDLE
_kernel.MapViewOfFile.argtypes = (wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t)
_kernel.MapViewOfFile.restype = ctypes.c_void_p
_kernel.UnmapViewOfFile.argtypes = (ctypes.c_void_p,)
_kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
_kernel.CreateEventW.argtypes = (ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR)
_kernel.CreateEventW.restype = wintypes.HANDLE
_kernel.SetEvent.argtypes = (wintypes.HANDLE,)
_kernel.ResetEvent.argtypes = (wintypes.HANDLE,)
_kernel.GetTickCount64.restype = ctypes.c_ulonglong
_user.GetForegroundWindow.restype = wintypes.HWND
_user.GetAncestor.argtypes = (wintypes.HWND, wintypes.UINT)
_user.GetAncestor.restype = wintypes.HWND
_user.GetWindow.argtypes = (wintypes.HWND, wintypes.UINT)
_user.GetWindow.restype = wintypes.HWND
_user.GetWindowThreadProcessId.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.DWORD))

_app = None
_handler = None
_event = None
_worker = None
_stop = None
_ready = None
_lock = threading.Lock()
_pending = False
_frame = None
_epoch = 0
_last_render = None
_errors_reported = set()


def _now():
    return int(_kernel.GetTickCount64())


def _foreground():
    hwnd = _user.GetForegroundWindow()
    if not hwnd or _user.GetAncestor(hwnd, 2) != hwnd or _user.GetWindow(hwnd, 4):
        return False
    pid = wintypes.DWORD()
    _user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value == os.getpid()


def _distance(new, old):
    return (new - old) & 0xffffffff


def _report(message):
    if message not in _errors_reported:
        _errors_reported.add(message)
        try:
            adsk.core.Application.log('SpaceKat Fusion: ' + message,
                                      adsk.core.LogLevels.InfoLogLevel, adsk.core.LogTypes.FileLogType)
        except Exception:
            pass


def _read_i32(address):
    return ctypes.c_int32.from_address(address).value


class _Mapping:
    def __init__(self):
        self.handle = _kernel.OpenFileMappingW(4, False, _MAP_NAME)
        self.address = None
        if self.handle:
            self.address = _kernel.MapViewOfFile(self.handle, 4, 0, 0, _MAP_SIZE)
            if not self.address:
                self.close()

    def close(self):
        if self.address:
            _kernel.UnmapViewOfFile(self.address)
            self.address = None
        if self.handle:
            _kernel.CloseHandle(self.handle)
            self.handle = None

    def snapshot(self, now):
        address = self.address
        before = _read_i32(address)
        if before & 1:
            return None
        data = ctypes.string_at(address, _MAP_SIZE)
        if before != _read_i32(address):
            return None
        magic, expires, sample = struct.unpack_from('<Iqq', data, 4)
        axes = struct.unpack_from('<6f', data, 24)
        head, floor = struct.unpack_from('<ii', data, 48)
        active = magic == _MAGIC and now < expires <= now + 1000
        motion_fresh = (0 < sample <= now and now - sample < _MAX_AGE_MS and
                        all(math.isfinite(axis) and abs(axis) <= 3.01 for axis in axes))
        if not motion_fresh:
            sample = 0
            axes = (0.0,) * 6
        return data, active, sample, axes, head, floor

    def button(self, data, sequence):
        offset = 56 + ((sequence - 1) & 31) * 24
        expected = ctypes.c_int32(sequence).value
        first, command, pid = struct.unpack_from('<iiI', data, offset)
        tick = struct.unpack_from('<q', data, offset + 16)[0]
        if first != expected or _read_i32(self.address + offset) != expected:
            return None
        return command, pid, tick


def _invalidate():
    global _frame, _epoch
    with _lock:
        _frame = None
        _epoch += 1


def _queue(commands, epoch):
    global _frame, _pending
    with _lock:
        previous = _frame
        if previous is not None and previous[1] == epoch:
            commands = (previous[0] + commands)[-_CAPACITY:]
        _frame = commands, epoch
        if _pending:
            return
        _pending = True
    try:
        if not _app.fireCustomEvent(_EVENT_ID):
            with _lock:
                _pending = False
    except Exception:
        with _lock:
            _pending = False


def _run_reader():
    cursor = None
    enabled = False
    try:
        while not _stop.wait(0.016):
            # Do not keep the mapping alive after its publisher has exited.
            mapping = _Mapping()
            if not mapping.address:
                mapping.close()
                cursor = None
                if enabled:
                    _invalidate()
                    enabled = False
                _stop.wait(0.2)
                continue
            try:
                now = _now()
                snapshot = mapping.snapshot(now)
                if snapshot is None:
                    continue
                data, active, _, axes, head, floor = snapshot
                active = active and _foreground()
                if not active:
                    cursor = head
                    if enabled:
                        _invalidate()
                        enabled = False
                    continue
                # A backwards head means the publisher restarted; discard its history.
                if cursor is not None and _distance(head, cursor) >= 0x80000000:
                    enabled = False
                    cursor = None
                if not enabled:
                    cursor = head
                    enabled = True
                    _invalidate()
                if 0 < _distance(floor, cursor) < 0x80000000:
                    cursor = floor
                available = _distance(head, cursor)
                if available > _CAPACITY:
                    cursor = ctypes.c_int32(head - _CAPACITY).value
                    available = _CAPACITY
                commands = []
                for unused in range(available):
                    cursor = ctypes.c_int32(cursor + 1).value
                    button = mapping.button(data, cursor)
                    if button:
                        command, pid, tick = button
                        if (command in _COMMANDS and pid == os.getpid() and
                                0 < tick <= now and now - tick < _MAX_AGE_MS):
                            commands.append((command, tick, cursor))
                with _lock:
                    already_pending = _pending
                if commands or already_pending or any(abs(axis) > 0.0001 for axis in axes):
                    with _lock:
                        epoch = _epoch
                    _queue(commands, epoch)
            finally:
                mapping.close()
    except Exception:
        # Do not call Fusion APIs from a worker thread.
        _invalidate()
        if _ready:
            _kernel.ResetEvent(_ready)

class _InputHandler(adsk.core.CustomEventHandler):
    def notify(self, args):
        global _pending, _frame, _last_render
        with _lock:
            _pending = False
            frame = _frame
            _frame = None
            epoch = _epoch
        now = _now()
        if not frame or frame[1] != epoch or not _foreground():
            _last_render = None
            return
        # Recheck the producer after the event waited in Fusion's queue.
        mapping = _Mapping()
        try:
            latest = mapping.snapshot(now) if mapping.address else None
        finally:
            mapping.close()
        if not latest or not latest[1]:
            _last_render = None
            return
        _, _, _, axes, head, floor = latest
        commands = [command for command, tick, sequence in frame[0]
                    if 0 < tick <= now and now - tick < _MAX_AGE_MS
                    and 0 < _distance(sequence, floor) < 0x80000000
                    and _distance(head, sequence) < _CAPACITY]
        if not commands and not any(abs(axis) > 0.0001 for axis in axes):
            _last_render = None
            return
        try:
            viewport = _app.activeViewport
            if not viewport:
                _last_render = None
                return
            current = time.monotonic()
            elapsed = current - _last_render if _last_render else 0.0
            dt = min(elapsed, 0.05) if 0.0 < elapsed <= 0.25 else 0.0
            _last_render = current
            if apply_view(viewport, axes, dt, commands):
                # Camera changes need a redraw when the user isn't moving the mouse.
                viewport.refresh()
                if any(axes):
                    _report('已应用六轴相机输入。')
        except Exception:
            _last_render = None
            _report('相机更新失败：' + traceback.format_exc())


def run(context):
    global _app, _handler, _event, _worker, _stop, _ready
    try:
        _app = adsk.core.Application.get()
        ctypes.set_last_error(0)
        _ready = _kernel.CreateEventW(None, True, False, _READY_NAME)
        if not _ready:
            raise ctypes.WinError(ctypes.get_last_error())
        if ctypes.get_last_error() == 183:
            _kernel.CloseHandle(_ready)
            _ready = None
            raise RuntimeError('SpaceKat Fusion 已在运行或就绪标志已被占用。')
        _event = _app.registerCustomEvent(_EVENT_ID)
        if not _event:
            raise RuntimeError('无法注册 Fusion 输入事件。')
        _handler = _InputHandler()
        if not _event.add(_handler):
            raise RuntimeError('无法绑定 Fusion 输入事件。')
        _stop = threading.Event()
        _worker = threading.Thread(target=_run_reader, name='SpaceKat Fusion input', daemon=True)
        _worker.start()
        if not _kernel.SetEvent(_ready):
            raise ctypes.WinError(ctypes.get_last_error())
    except Exception:
        if _app:
            _report('启动失败：' + traceback.format_exc())
        stop(context)


def stop(context):
    global _app, _handler, _event, _worker, _stop, _ready, _pending, _frame, _last_render
    if _ready:
        _kernel.ResetEvent(_ready)
    if _stop:
        _stop.set()
    if _worker:
        _worker.join()
    if _event and _handler:
        try:
            _event.remove(_handler)
        except Exception:
            pass
    if _app and _event:
        try:
            _app.unregisterCustomEvent(_EVENT_ID)
        except Exception:
            pass
    if _ready:
        _kernel.CloseHandle(_ready)
    with _lock:
        _frame = None
        _pending = False
    _last_render = None
    _ready = _worker = _stop = _handler = _event = _app = None
