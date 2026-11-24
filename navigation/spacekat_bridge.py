"""Read six shaped axes from SpaceKat 小助手 in the current Windows session."""
import ctypes
import math
import os
import struct
import time
from ctypes import wintypes


BLENDER = 1
MAP = r"Local\SpaceKat.Navigation.v1"
FRAME = struct.Struct("<III4xQQ6f")  # Sequence, SKN1, target, deadlines, X..Rz.
MAGIC = 0x314E4B53
ZERO = (0.0,) * 6

if os.name == "nt":
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    user = ctypes.WinDLL("user32", use_last_error=True)
    kernel.OpenFileMappingW.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR)
    kernel.OpenFileMappingW.restype = wintypes.HANDLE
    kernel.MapViewOfFile.argtypes = (wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t)
    kernel.MapViewOfFile.restype = ctypes.c_void_p
    kernel.UnmapViewOfFile.argtypes = (ctypes.c_void_p,)
    kernel.UnmapViewOfFile.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    kernel.CloseHandle.restype = wintypes.BOOL
    kernel.GetTickCount64.restype = ctypes.c_uint64
    user.GetForegroundWindow.restype = wintypes.HWND
    user.GetWindow.argtypes = (wintypes.HWND, wintypes.UINT)
    user.GetWindow.restype = wintypes.HWND
    user.GetWindowThreadProcessId.argtypes = (wintypes.HWND, ctypes.POINTER(wintypes.DWORD))
    user.GetWindowThreadProcessId.restype = wintypes.DWORD


def foreground_window():
    if os.name != "nt":
        return None
    hwnd = user.GetForegroundWindow()
    if not hwnd or user.GetWindow(hwnd, 4):  # Ignore owned native dialogs (GW_OWNER).
        return None
    pid = wintypes.DWORD()
    return hwnd if user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid)) and pid.value == os.getpid() else None


def decode(payload, after, target, now):
    """None means disabled; an all-zero tuple means armed but stationary."""
    sequence, magic, destination, until, sample_until, *axes = FRAME.unpack(payload)
    if sequence & 1 or sequence != after or magic != MAGIC or target != destination:
        return None
    if not now < until <= now + 125:
        return None
    if not now < sample_until <= until or any(not math.isfinite(x) or abs(x) > 3 for x in axes):
        return ZERO
    return tuple(axes)


def read(target, name=MAP, require_foreground=True):
    if os.name != "nt" or (require_foreground and not foreground_window()):
        return None
    handle = kernel.OpenFileMappingW(4, False, name)  # FILE_MAP_READ
    if not handle:
        return None
    try:
        address = kernel.MapViewOfFile(handle, 4, 0, 0, FRAME.size)
        if not address:
            return None
        try:
            before = ctypes.c_uint32.from_address(address).value
            if before & 1:
                return None
            payload = ctypes.string_at(address, FRAME.size)
            after = ctypes.c_uint32.from_address(address).value
            if before != after:
                return None
            return decode(payload, after, target, kernel.GetTickCount64())
        finally:
            kernel.UnmapViewOfFile(address)
    finally:
        kernel.CloseHandle(handle)


class StepClock:
    def __init__(self):
        self.previous = None

    def step(self, axes):
        if axes is None or not any(axes):
            self.previous = None
            return 0
        now = time.monotonic()
        elapsed = 0 if self.previous is None or now - self.previous > 0.12 else min(0.04, now - self.previous)
        self.previous = now
        return elapsed


def motion(axes, dt):
    x, y, z, rx, ry, rz = axes
    # Positive X/Y move the model right/up. Positive Z zooms in.
    return (-x * dt, -y * dt, math.exp(-1.8 * z * dt), (rx * dt * 1.8, ry * dt * 1.8, rz * dt * 1.8))
