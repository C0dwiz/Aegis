using System;
using System.Threading;

namespace Aegis.Common;

/// <summary>
/// High-performance ID generator inspired by Twitter Snowflake
/// Generates 64-bit unique IDs with timestamp, node ID, and sequence
/// </summary>
public sealed class FastIdGenerator
{
    private const long Epoch = 1704067200000L; // 2024-01-01 00:00:00 UTC

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
        if (nodeId < 0 || nodeId >= (1L << NodeBits))
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId), $"Node ID must be between 0 and {(1L << NodeBits) - 1}");
        }
        _nodeId = nodeId;
    }

    /// <summary>
    /// Generates the next unique ID
    /// </summary>
    /// <returns>A unique 64-bit ID</returns>
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

    /// <summary>
    /// Extracts timestamp from ID
    /// </summary>
    public static DateTime GetTimestampFromId(long id)
    {
        var timestamp = (id >> TimeShift) + Epoch;
        return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
    }

    /// <summary>
    /// Extracts node ID from ID
    /// </summary>
    public static int GetNodeIdFromId(long id)
    {
        return (int)((id >> NodeShift) & ((1L << NodeBits) - 1));
    }

    /// <summary>
    /// Extracts sequence number from ID
    /// </summary>
    public static long GetSequenceFromId(long id)
    {
        return id & MaxSequence;
    }

    private static long CurrentTime()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

/// <summary>
/// Singleton service for generating different types of IDs
/// </summary>
public interface IIdGenerator
{
    /// <summary>
    /// Generates a new user ID
    /// </summary>
    ulong GenerateUserId();

    /// <summary>
    /// Generates a new chat ID (for private chats and groups)
    /// </summary>
    ulong GenerateChatId();

    /// <summary>
    /// Generates a new channel ID
    /// </summary>
    ulong GenerateChannelId();

    /// <summary>
    /// Generates a new message ID
    /// </summary>
    ulong GenerateMessageId();
}

public class IdGenerator : IIdGenerator
{
    private readonly FastIdGenerator _userIdGenerator;
    private readonly FastIdGenerator _chatIdGenerator;
    private readonly FastIdGenerator _channelIdGenerator;
    private readonly FastIdGenerator _messageIdGenerator;

    public IdGenerator()
    {
        // Use different node IDs for different types to avoid collisions
        _userIdGenerator = new FastIdGenerator(1);
        _chatIdGenerator = new FastIdGenerator(2);
        _channelIdGenerator = new FastIdGenerator(3);
        _messageIdGenerator = new FastIdGenerator(4);
    }

    public ulong GenerateUserId() => (ulong)_userIdGenerator.NextId();
    public ulong GenerateChatId() => (ulong)_chatIdGenerator.NextId();
    public ulong GenerateChannelId() => (ulong)_channelIdGenerator.NextId();
    public ulong GenerateMessageId() => (ulong)_messageIdGenerator.NextId();
}
