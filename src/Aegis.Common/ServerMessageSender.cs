using Aegis.Common.Logging;

namespace Aegis.Common;

public interface IMessageSender
{
    Task SendMessageAsync(ulong connectionId, byte[] encryptedMessage);
}
