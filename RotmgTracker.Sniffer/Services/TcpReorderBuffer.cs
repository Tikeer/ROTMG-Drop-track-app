using System;
using System.Collections.Generic;

namespace RotmgTrackerV1.Sniffer.Services
{
    /// <summary>
    /// Składa strumień TCP w jednym kierunku w kolejności. Segmenty "z przyszłości"
    /// czekają MaxWaitMs, zanim luka zostanie uznana za utraconą.
    /// </summary>
    public sealed class TcpReorderBuffer
    {
        private struct Segment
        {
            public uint Seq;
            public byte[] Data;
        }

        private readonly List<Segment> _pending = new();
        private uint _expected;
        private bool _initialized;
        private long _gapSinceMs = -1;
        private int _pendingBytes;

        public int MaxWaitMs { get; set; } = 400;
        public int MaxPendingBytes { get; set; } = 512 * 1024;
        public bool IsInitialized => _initialized;

        // Diagnostyka: ile segmentów musiało czekać na brakujący poprzednik / ile było duplikatów
        public int HeldSegments { get; private set; }
        public int DuplicateSegments { get; private set; }

        public void Reset()
        {
            _pending.Clear();
            _pendingBytes = 0;
            _gapSinceMs = -1;
            _initialized = false;
            _expected = 0;
            HeldSegments = 0;
            DuplicateSegments = 0;
        }

        /// <summary>
        /// Ustawia numer pierwszego oczekiwanego bajtu (ISN + 1 z SYN). Ignorowane, gdy już zainicjowany.
        /// </summary>
        public void Initialize(uint expectedSeq)
        {
            if (_initialized)
                return;

            _expected = expectedSeq;
            _initialized = true;
        }

        /// <summary>
        /// Zwraca fragmenty gotowe do dekodowania, w kolejności.
        /// gapBytes > 0: luka uznana za utraconą (strumień RC4 jest zepsuty); zwrócone dane
        /// to tylko to, co leżało PRZED luką, a reszta oczekujących segmentów jest odrzucana.
        /// </summary>
        public List<byte[]> Push(uint seq, byte[] payload, long nowMs, out int gapBytes)
        {
            gapBytes = 0;
            var output = new List<byte[]>();
            if (payload == null || payload.Length == 0)
                return output;

            if (!_initialized)
            {
                _expected = seq;
                _initialized = true;
            }

            Accept(seq, payload, output);
            Drain(output);

            // Postęp = poprzednia luka została wypełniona; czas kolejnej luki liczymy od nowa
            if (output.Count > 0)
                _gapSinceMs = -1;

            if (_pending.Count == 0)
            {
                _gapSinceMs = -1;
                return output;
            }

            if (_gapSinceMs < 0)
                _gapSinceMs = nowMs;

            if (nowMs - _gapSinceMs >= MaxWaitMs || _pendingBytes > MaxPendingBytes)
            {
                gapBytes = NearestPendingGap();
                _pending.Clear();
                _pendingBytes = 0;
                _gapSinceMs = -1;
            }

            return output;
        }

        private int NearestPendingGap()
        {
            int min = int.MaxValue;
            foreach (var s in _pending)
            {
                int d = unchecked((int)(s.Seq - _expected));
                if (d < min) min = d;
            }

            return min;
        }

        private void Accept(uint seq, byte[] payload, List<byte[]> output)
        {
            int diff = unchecked((int)(seq - _expected));
            if (diff <= 0)
            {
                int skip = -diff;
                if (skip >= payload.Length)
                {
                    DuplicateSegments++; // duplikat / retransmisja / kopia z drugiego interfejsu
                    return;
                }

                byte[] data = skip == 0 ? payload : payload.AsSpan(skip).ToArray();
                output.Add(data);
                _expected = unchecked(_expected + (uint)data.Length);
                return;
            }

            foreach (var s in _pending)
            {
                if (s.Seq == seq && s.Data.Length >= payload.Length)
                {
                    DuplicateSegments++; // ten sam segment z drugiego interfejsu
                    return;
                }
            }

            _pending.Add(new Segment { Seq = seq, Data = payload });
            _pendingBytes += payload.Length;
            HeldSegments++;
        }

        private void Drain(List<byte[]> output)
        {
            bool progress;
            do
            {
                progress = false;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var seg = _pending[i];
                    int diff = unchecked((int)(seg.Seq - _expected));
                    if (diff > 0)
                        continue;

                    _pending.RemoveAt(i);
                    _pendingBytes -= seg.Data.Length;

                    int skip = -diff;
                    if (skip < seg.Data.Length)
                    {
                        byte[] data = skip == 0 ? seg.Data : seg.Data.AsSpan(skip).ToArray();
                        output.Add(data);
                        _expected = unchecked(_expected + (uint)data.Length);
                    }

                    progress = true;
                    break;
                }
            } while (progress);
        }
    }
}