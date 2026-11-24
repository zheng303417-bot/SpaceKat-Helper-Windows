using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpaceKatMonitor
{
    internal enum FusionViewCommand { None = 0, Fit = 2, Top = 3, Right = 5, Front = 6 }

    // Private mapping shared only by one helper run and its Fusion worker.
    internal sealed class FusionNativeBridge : IDisposable
    {
        internal const string NamePrefix = @"Local\SpaceKat.FusionInput.v2.";
        internal const int ButtonCapacity = 32;
        private const int ButtonHeadOffset = 48;
        private const int DiscardedButtonsOffset = 52;
        private const int ButtonStartOffset = 56;
        private const int ButtonEntrySize = 24;
        private const int Size = ButtonStartOffset + ButtonCapacity * ButtonEntrySize;
        private const int Magic = 0x32464B53; // SKF2
        internal const int SampleTimeout = 150;
        private const int HeartbeatTimeout = 125;
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;
        private int _sequence;
        private int _buttonHead;

        [DllImport("kernel32.dll")]
        internal static extern long GetTickCount64();

        internal FusionNativeBridge(string name, bool writer)
        {
            _map = writer ? MemoryMappedFile.CreateNew(name, Size) :
                MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            try { _view = _map.CreateViewAccessor(0, Size, writer ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read); }
            catch { _map.Dispose(); throw; }
        }

        internal void Publish(long sampleTime, float[] axes)
        {
            long now = GetTickCount64();
            if (axes == null || sampleTime <= 0 || sampleTime > now || now - sampleTime >= SampleTimeout)
                sampleTime = 0;
            _view.Write(0, unchecked(++_sequence));
            Thread.MemoryBarrier();
            _view.Write(4, Magic);
            _view.Write(8, axes == null ? 0 : now + HeartbeatTimeout);
            _view.Write(16, sampleTime);
            for (int i = 0; i < 6; i++) _view.Write(24 + i * 4, sampleTime != 0 && axes != null ? axes[i] : 0f);
            if (axes == null) _view.Write(DiscardedButtonsOffset, _buttonHead);
            Thread.MemoryBarrier();
            _view.Write(0, unchecked(++_sequence));
        }

        internal bool TryRead(long now, float[] axes, out long sampleTime)
        {
            sampleTime = 0;
            int before = _view.ReadInt32(0);
            if ((before & 1) != 0) return false;
            Thread.MemoryBarrier();
            int magic = _view.ReadInt32(4);
            long expires = _view.ReadInt64(8);
            long received = _view.ReadInt64(16);
            if (magic != Magic || expires <= now || received <= 0 || received > now || now - received >= SampleTimeout)
                return false;
            for (int i = 0; i < 6; i++)
            {
                float axis = _view.ReadSingle(24 + i * 4);
                if (Single.IsNaN(axis) || Single.IsInfinity(axis)) return false;
                axes[i] = axis;
            }
            Thread.MemoryBarrier();
            if (_view.ReadInt32(0) != before) return false;
            sampleTime = received;
            return true;
        }

        internal bool TryReadActiveButtons(long now, out int discardedThrough)
        {
            int before = _view.ReadInt32(0);
            Thread.MemoryBarrier();
            bool active = (before & 1) == 0 && _view.ReadInt32(4) == Magic && _view.ReadInt64(8) > now;
            discardedThrough = _view.ReadInt32(DiscardedButtonsOffset);
            Thread.MemoryBarrier();
            return active && _view.ReadInt32(0) == before;
        }

        // Button presses are queued separately so a tap between two motion frames survives.
        internal void PublishButton(int command, uint foregroundPid, long time)
        {
            int next = unchecked(_buttonHead + 1);
            int offset = ButtonStartOffset + ((next - 1) & (ButtonCapacity - 1)) * ButtonEntrySize;
            _view.Write(offset, ~next);
            Thread.MemoryBarrier();
            _view.Write(offset + 4, command);
            _view.Write(offset + 8, foregroundPid);
            _view.Write(offset + 16, time);
            Thread.MemoryBarrier();
            _view.Write(offset, next);
            Thread.MemoryBarrier();
            _view.Write(ButtonHeadOffset, next);
            _buttonHead = next;
        }

        internal int ButtonHead
        {
            get
            {
                int head = _view.ReadInt32(ButtonHeadOffset);
                Thread.MemoryBarrier();
                return head;
            }
        }

        internal bool TryReadButton(int number, out int command, out uint foregroundPid, out long time)
        {
            int offset = ButtonStartOffset + ((number - 1) & (ButtonCapacity - 1)) * ButtonEntrySize;
            int before = _view.ReadInt32(offset);
            Thread.MemoryBarrier();
            command = _view.ReadInt32(offset + 4);
            foregroundPid = _view.ReadUInt32(offset + 8);
            time = _view.ReadInt64(offset + 16);
            Thread.MemoryBarrier();
            return before == number && _view.ReadInt32(offset) == before;
        }

        public void Dispose()
        {
            try { _view.Dispose(); }
            finally { _map.Dispose(); }
        }
    }
}
