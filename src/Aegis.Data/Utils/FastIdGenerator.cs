using System;
using System.Threading;

namespace Aegis.Data.Utils
{
    /// <summary>
    /// Fast, thread-safe, distributed ID generator (Snowflake-like)
    /// </summary>
    public sealed class FastIdGenerator
    {
        private const long Epoch = 1704067200000L; // 2024-01-01
        private const int NodeBits = 10;
        private const int SequenceBits = 12;
        private const long MaxSequence = (1L << SequenceBits) - 1;
        private const int NodeShift = SequenceBits;
        private const int TimeShift = SequenceBits + NodeBits;
        private long _lastTimestamp = -1;
        private long _sequence = 0;
        private readonly long _nodeId;

        public FastIdGenerator(int nodeId)
        {
            if (nodeId < 0 || nodeId > ((1 << NodeBits) - 1))
                throw new ArgumentOutOfRangeException(nameof(nodeId), $"NodeId must be between 0 and {(1 << NodeBits) - 1}");
            _nodeId = nodeId;
        }

        public long NextId()
        {
            while (true)
            {
                long timestamp = CurrentTime();
                long last = Volatile.Read(ref _lastTimestamp);
                if (timestamp == last)
                {
                    long seq = (Interlocked.Increment(ref _sequence)) & MaxSequence;
                    if (seq == 0)
                        continue;
                    return ((timestamp - Epoch) << TimeShift)
                           | (_nodeId << NodeShift)
                           | seq;
                }
                if (Interlocked.CompareExchange(ref _lastTimestamp, timestamp, last) == last)
                {
                    Interlocked.Exchange(ref _sequence, 0);
                    return ((timestamp - Epoch) << TimeShift)
                           | (_nodeId << NodeShift);
                }
            }
        }

        private static long CurrentTime()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
