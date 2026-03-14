using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Common;
using Aegis.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Handlers;

public class MessageRouter
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly Dictionary<Aegis.Protocol.MessageType, IMessageHandler>? _handlers;
    private readonly ILogger _logger;
    private readonly IMessageSender _messageSender;
    
    [ActivatorUtilitiesConstructor]
    public MessageRouter(IServiceProvider serviceProvider, IMessageSender messageSender, ILogger? logger = null)
    {
        _serviceProvider = serviceProvider;
        _handlers = null;
        _logger = logger ?? new Aegis.Transport.NullLogger();
        _messageSender = messageSender;
    }

    private MessageRouter(Dictionary<Aegis.Protocol.MessageType, IMessageHandler> handlers, IMessageSender messageSender, ILogger? logger = null)
    {
        _serviceProvider = null;
        _handlers = handlers;
        _logger = logger ?? new Aegis.Transport.NullLogger();
        _messageSender = messageSender;
    }

    public static MessageRouter ForHandlers(IEnumerable<IMessageHandler> handlers, IMessageSender messageSender, ILogger? logger = null)
    {
        return new MessageRouter(
            handlers.ToDictionary(h => h.Type, h => h),
            messageSender,
            logger
        );
    }
    
    public async ValueTask RouteAsync(ConnectionContext context, Message message)
    {
        var handler = ResolveHandler(message.Type);
        if (handler != null)
        {
            await handler.HandleAsync(context, message);
        }
        else
        {
            _logger.Error($"Unknown message type: {message.Type}");
            // отправка ошибки
            await SendErrorAsync(context, message.SequenceId, $"Unknown message type: {message.Type}");
        }
    }

    private IMessageHandler? ResolveHandler(Aegis.Protocol.MessageType type)
    {
        if (_handlers != null && _handlers.TryGetValue(type, out var directHandler))
        {
            return directHandler;
        }

        if (_serviceProvider == null)
        {
            return null;
        }

        return type switch
        {
            Aegis.Protocol.MessageType.Handshake => _serviceProvider.GetRequiredService<HandshakeHandler>(),
            Aegis.Protocol.MessageType.Auth => _serviceProvider.GetRequiredService<AuthHandler>(),
            Aegis.Protocol.MessageType.Ping => _serviceProvider.GetRequiredService<PingHandler>(),
            Aegis.Protocol.MessageType.Message => _serviceProvider.GetRequiredService<MessageHandler>(),
            Aegis.Protocol.MessageType.Ack => _serviceProvider.GetRequiredService<AckHandler>(),
            Aegis.Protocol.MessageType.Nack => _serviceProvider.GetRequiredService<NackHandler>(),
            Aegis.Protocol.MessageType.RetransmitRequest => _serviceProvider.GetRequiredService<RetransmitRequestHandler>(),
            Aegis.Protocol.MessageType.Register => _serviceProvider.GetRequiredService<RegistrationHandler>(),
            Aegis.Protocol.MessageType.UserPresence => _serviceProvider.GetRequiredService<UserPresenceHandler>(),
            Aegis.Protocol.MessageType.UserSearch => _serviceProvider.GetRequiredService<UserSearchHandler>(),
            Aegis.Protocol.MessageType.ChannelMessage => _serviceProvider.GetRequiredService<ChannelMessageHandler>(),
            Aegis.Protocol.MessageType.ChannelCreate => _serviceProvider.GetRequiredService<ChannelCreateHandler>(),
            Aegis.Protocol.MessageType.ChannelJoin => _serviceProvider.GetRequiredService<ChannelJoinHandler>(),
            Aegis.Protocol.MessageType.PrivateChatMessage => _serviceProvider.GetRequiredService<PrivateChatMessageHandler>(),
            Aegis.Protocol.MessageType.ChatListRequest => _serviceProvider.GetRequiredService<ChatListHandler>(),
            Aegis.Protocol.MessageType.PrivateChatHistoryRequest => _serviceProvider.GetRequiredService<PrivateChatHistoryHandler>(),
            Aegis.Protocol.MessageType.ChannelHistoryRequest => _serviceProvider.GetRequiredService<ChannelHistoryHandler>(),
            Aegis.Protocol.MessageType.ProfileUpdate => _serviceProvider.GetRequiredService<ProfileUpdateHandler>(),
            Aegis.Protocol.MessageType.ProfileGet => _serviceProvider.GetRequiredService<ProfileGetHandler>(),
            Aegis.Protocol.MessageType.ProfileAvatarAdd => _serviceProvider.GetRequiredService<ProfileAvatarAddHandler>(),
            Aegis.Protocol.MessageType.ProfileAvatarList => _serviceProvider.GetRequiredService<ProfileAvatarListHandler>(),
            Aegis.Protocol.MessageType.ProfileAvatarDelete => _serviceProvider.GetRequiredService<ProfileAvatarDeleteHandler>(),
            Aegis.Protocol.MessageType.ProfileAvatarSetPrimary => _serviceProvider.GetRequiredService<ProfileAvatarSetPrimaryHandler>(),
            Aegis.Protocol.MessageType.ChannelLinkUpdate => _serviceProvider.GetRequiredService<ChannelLinkUpdateHandler>(),
            Aegis.Protocol.MessageType.ChannelLinkGet => _serviceProvider.GetRequiredService<ChannelLinkGetHandler>(),
            Aegis.Protocol.MessageType.ChannelResolve => _serviceProvider.GetRequiredService<ChannelResolveHandler>(),
            Aegis.Protocol.MessageType.ChannelJoinByLink => _serviceProvider.GetRequiredService<ChannelJoinByLinkHandler>(),
            Aegis.Protocol.MessageType.MessageEdit => _serviceProvider.GetRequiredService<MessageEditHandler>(),
            Aegis.Protocol.MessageType.MessageDelete => _serviceProvider.GetRequiredService<MessageDeleteHandler>(),
            Aegis.Protocol.MessageType.ChannelEdit => _serviceProvider.GetRequiredService<ChannelEditHandler>(),
            Aegis.Protocol.MessageType.GroupCreate => _serviceProvider.GetRequiredService<GroupCreateHandler>(),
            Aegis.Protocol.MessageType.GroupEdit => _serviceProvider.GetRequiredService<GroupEditHandler>(),
            Aegis.Protocol.MessageType.GroupMessageSend => _serviceProvider.GetRequiredService<GroupMessageSendHandler>(),
            Aegis.Protocol.MessageType.MemberRoleUpdate => _serviceProvider.GetRequiredService<MemberRoleUpdateHandler>(),
            Aegis.Protocol.MessageType.MemberPermissionUpdate => _serviceProvider.GetRequiredService<MemberPermissionUpdateHandler>(),
            _ => null
        };
    }
    
    private async Task SendErrorAsync(ConnectionContext context, ulong sequenceId, string error)
    {
        var errorMsg = new Message
        {
            Magic = ProtocolConstants.Magic,
            VersionMajor = ProtocolConstants.VersionMajor,
            VersionMinor = ProtocolConstants.VersionMinor,
            Type = Aegis.Protocol.MessageType.Error,
            SequenceId = sequenceId,
            PayloadLength = (uint)System.Text.Encoding.UTF8.GetByteCount(error),
            Payload = System.Text.Encoding.UTF8.GetBytes(error)
        };

        await _messageSender.SendProtocolMessageAsync(
            context.ConnectionId,
            (ushort)Aegis.Protocol.MessageType.Error,
            sequenceId,
            System.Text.Encoding.UTF8.GetBytes(error),
            allowUnsigned: true);
    }
}
