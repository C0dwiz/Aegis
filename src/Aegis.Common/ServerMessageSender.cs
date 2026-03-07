using Aegis.Common.Logging;

namespace Aegis.Common;

public interface IMessageSender
{
    Task SendMessageAsync(ulong connectionId, byte[] encryptedMessage);
    Task SendProtocolMessageAsync(ulong connectionId, ushort messageType, ulong sequenceId, byte[] payload, bool allowUnsigned = false);
}
