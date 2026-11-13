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

        private readonly List<Segment> _pending = new List<Segment>();
        private uint _expected;
        private bool _initialized;
        private long _gapSinceMs = -1;
        private int _pendingBytes;

        public int MaxWaitMs { get; set; } = 200;
        public int MaxPendingBytes { get; set; } = 512 * 1024;
        public bool IsInitialized => _initialized;

        public void Reset()
        {
            _pending.Clear();
            _pendingBytes = 0;
            _gapSinceMs = -1;
            _initialized = false;
            _expected = 0;
        }

        /// <summary>
        /// Zwraca fragmenty gotowe do dekodowania, w kolejności.
        /// gapBytes > 0 oznacza, że luka została uznana za utraconą (strumień RC4 jest zepsuty).
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

            if (_pending.Count > 0)
            {
                if (_gapSinceMs < 0)
                    _gapSinceMs = nowMs;

                if (nowMs - _gapSinceMs >= MaxWaitMs || _pendingBytes > MaxPendingBytes)
                {
                    int min = int.MaxValue;
                    foreach (var s in _pending)
                    {
                        int d = (int)(s.Seq - _expected);
                        if (d < min) min = d;
                    }

                    gapBytes = min;
                    _expected += (uint)min;
                    Drain(output);
                    _gapSinceMs = _pending.Count > 0 ? nowMs : -1;
                }
            }
            else
            {
                _gapSinceMs = -1;
            }

            return output;
        }

        private void Accept(uint seq, byte[] payload, List<byte[]> output)
        {
            int diff = (int)(seq - _expected);
            if (diff <= 0)
            {
                int skip = -diff;
                if (skip >= payload.Length)
                    return; // duplikat / retransmisja

                byte[] data = skip == 0 ? payload : payload[skip..];
                output.Add(data);
                _expected += (uint)data.Length;
                return;
            }

            foreach (var s in _pending)
            {
                if (s.Seq == seq && s.Data.Length >= payload.Length)
                    return; // ten sam segment z drugiego interfejsu
            }

            _pending.Add(new Segment { Seq = seq, Data = payload });
            _pendingBytes += payload.Length;
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
                    int diff = (int)(seg.Seq - _expected);
                    if (diff > 0)
                        continue;

                    _pending.RemoveAt(i);
                    _pendingBytes -= seg.Data.Length;

                    int skip = -diff;
                    if (skip < seg.Data.Length)
                    {
                        byte[] data = skip == 0 ? seg.Data : seg.Data[skip..];
                        output.Add(data);
                        _expected += (uint)data.Length;
                    }

                    progress = true;
                    break;
                }
            } while (progress);
        }
    }
}