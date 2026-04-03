using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Aegis.Crypto;

public sealed class DoubleRatchetAlgorithm
{
    private const int KeySizeBytes = 32;
    private const int MaxSkippedKeys = 100;

    private readonly object _sync = new();
    private readonly ConcurrentDictionary<uint, byte[]> _skippedReceivingKeys = new();

    private byte[] _rootKey = new byte[KeySizeBytes];
    private byte[] _sendingChainKey = new byte[KeySizeBytes];
    private byte[] _receivingChainKey = new byte[KeySizeBytes];
    private uint _nextSendingMessageNumber;
    private uint _nextReceivingMessageNumber;

    public sealed record StateSnapshot(
        byte[] RootKey,
        byte[] SendingChainKey,
        byte[] ReceivingChainKey,
        uint NextSendingMessageNumber,
        uint NextReceivingMessageNumber);

    public void Initialize(
        ReadOnlySpan<byte> rootKey,
        ReadOnlySpan<byte> sendingChainKey,
        ReadOnlySpan<byte> receivingChainKey,
        uint nextSendingMessageNumber = 0,
        uint nextReceivingMessageNumber = 0)
    {
        if (rootKey.Length != KeySizeBytes || sendingChainKey.Length != KeySizeBytes || receivingChainKey.Length != KeySizeBytes)
        {
            throw new ArgumentException("Double Ratchet expects 32-byte keys");
        }

        lock (_sync)
        {
            rootKey.CopyTo(_rootKey);
            sendingChainKey.CopyTo(_sendingChainKey);
            receivingChainKey.CopyTo(_receivingChainKey);
            _nextSendingMessageNumber = nextSendingMessageNumber;
            _nextReceivingMessageNumber = nextReceivingMessageNumber;
            ClearSkippedKeys();
        }
    }

    public StateSnapshot ExportState()
    {
        lock (_sync)
        {
            return new StateSnapshot(
                _rootKey.ToArray(),
                _sendingChainKey.ToArray(),
                _receivingChainKey.ToArray(),
                _nextSendingMessageNumber,
                _nextReceivingMessageNumber);
        }
    }

    public (uint MessageNumber, byte[] MessageKey) NextSendingMessageKey()
    {
        lock (_sync)
        {
            var messageNumber = _nextSendingMessageNumber;
            var messageKey = Kdf(_sendingChainKey, 0x01);
            _sendingChainKey = Kdf(_sendingChainKey, 0x02);
            _nextSendingMessageNumber++;
            return (messageNumber, messageKey);
        }
    }

    public bool TryGetReceivingMessageKey(uint messageNumber, out byte[] messageKey)
    {
        lock (_sync)
        {
            if (_skippedReceivingKeys.TryRemove(messageNumber, out var skipped))
            {
                messageKey = skipped;
                return true;
            }

            if (messageNumber < _nextReceivingMessageNumber)
            {
                messageKey = Array.Empty<byte>();
                return false;
            }

            while (_nextReceivingMessageNumber < messageNumber)
            {
                var skippedNumber = _nextReceivingMessageNumber;
                var skippedKey = Kdf(_receivingChainKey, 0x01);
                _receivingChainKey = Kdf(_receivingChainKey, 0x02);
                _nextReceivingMessageNumber++;
                AddSkippedKey(skippedNumber, skippedKey);
            }

            messageKey = Kdf(_receivingChainKey, 0x01);
            _receivingChainKey = Kdf(_receivingChainKey, 0x02);
            _nextReceivingMessageNumber++;
            return true;
        }
    }

    public void RatchetStep(ReadOnlySpan<byte> newRootKey, ReadOnlySpan<byte> newSendingChainKey, ReadOnlySpan<byte> newReceivingChainKey)
    {
        if (newRootKey.Length != KeySizeBytes || newSendingChainKey.Length != KeySizeBytes || newReceivingChainKey.Length != KeySizeBytes)
        {
            throw new ArgumentException("Double Ratchet expects 32-byte keys");
        }

        lock (_sync)
        {
            newRootKey.CopyTo(_rootKey);
            newSendingChainKey.CopyTo(_sendingChainKey);
            newReceivingChainKey.CopyTo(_receivingChainKey);
            _nextSendingMessageNumber = 0;
            _nextReceivingMessageNumber = 0;
            ClearSkippedKeys();
        }
    }

    private static byte[] Kdf(ReadOnlySpan<byte> key, byte label)
    {
        Span<byte> input = stackalloc byte[1];
        input[0] = label;
        return HMACSHA256.HashData(key, input);
    }

    private void AddSkippedKey(uint messageNumber, byte[] key)
    {
        _skippedReceivingKeys[messageNumber] = key;
        if (_skippedReceivingKeys.Count <= MaxSkippedKeys)
        {
            return;
        }

        var oldest = _skippedReceivingKeys.Keys.OrderBy(x => x).First();
        _skippedReceivingKeys.TryRemove(oldest, out _);
    }

    private void ClearSkippedKeys()
    {
        foreach (var key in _skippedReceivingKeys.Keys)
        {
            _skippedReceivingKeys.TryRemove(key, out _);
        }
    }
}
