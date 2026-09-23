using System;

namespace Thermodynamics.Presentation
{
    public sealed class ThermalVisionRayScan<T>
    {
        public struct Ticket
        {
            internal ThermalVisionRayScan<T> Owner;
            internal int Slot;
            internal long Serial;
            public int Pixel { get; internal set; }
        }

        private struct Pending
        {
            public long Serial;
            public object Generation;
            public int Pixel;
        }

        private readonly Pending[] pending;
        private readonly int[] free;
        private readonly int perFrame;
        private T[] staging, published;
        private int freeCount, remaining, next, received;
        private long serial, lastFrame = long.MinValue;
        private object generation;
        private bool active;

        public int SampleCount { get { return staging.Length; } }
        public int Outstanding { get { return pending.Length - freeCount; } }
        public int Received { get { return received; } }
        public bool HasFrame { get; private set; }


        public ThermalVisionRayScan(int sampleCount, int maxOutstanding, int queriesPerFrame)
        {
            if (sampleCount < 1 || sampleCount > 65536 || maxOutstanding < 1 || maxOutstanding > 256
                || queriesPerFrame < 1 || queriesPerFrame > 4096)
                throw new ArgumentException("Invalid bounded sensor scan capacity.");
            staging = new T[sampleCount];
            published = new T[sampleCount];
            pending = new Pending[maxOutstanding];
            free = new int[maxOutstanding];
            for (int i = 0; i < free.Length; i++) free[i] = i;
            freeCount = free.Length;
            perFrame = queriesPerFrame;
        }


        public void Begin()
        {
            Invalidate();

            generation = new object();
            active = true;
        }


        public void Invalidate()
        {
            active = false;
            generation = null;
            HasFrame = false;
            next = received = 0;
        }


        public void AdvanceFrame(long frame)
        {
            if (frame <= lastFrame) return;
            lastFrame = frame;
            remaining = perFrame;
        }


        public bool TryIssue(out Ticket ticket)
        {

            ticket = new Ticket();
            if (!active || next == SampleCount || remaining == 0 || freeCount == 0) return false;
            if (serial == long.MaxValue) throw new InvalidOperationException("Sensor ticket sequence exhausted.");
            int slot = free[--freeCount];
            pending[slot] = new Pending { Serial = ++serial, Generation = generation, Pixel = next };
            ticket = new Ticket { Owner = this, Slot = slot, Serial = serial, Pixel = next++ };
            remaining--;
            return true;
        }


        public bool Complete(Ticket ticket, T value)
        {
            if (ticket.Owner != this || ticket.Slot < 0 || ticket.Slot >= pending.Length) return false;
            Pending entry = pending[ticket.Slot];
            if (entry.Serial == 0 || entry.Serial != ticket.Serial) return false;

            pending[ticket.Slot] = new Pending();
            free[freeCount++] = ticket.Slot;
            if (!active || entry.Generation != generation) return false;
            staging[entry.Pixel] = value;
            received++;
            if (received == SampleCount)
            {
                T[] old = published;
                published = staging;
                staging = old;
                HasFrame = true;
                active = false;
            }
            return true;
        }


        public bool TryRead(int pixel, out T value)
        {

            value = default(T);
            if (!HasFrame || pixel < 0 || pixel >= SampleCount) return false;
            value = published[pixel];
            return true;
        }
    }
}
