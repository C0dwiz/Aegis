using Aegis.Protocol;
using Aegis.DomainRules;
using Aegis.Transport;
using Aegis.Common.Logging;
using Aegis.Common;
using Aegis.Crypto;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Handlers;

public class MessageRouter
{
    private static readonly IReadOnlyDictionary<Aegis.Protocol.MessageType, Func<IServiceProvider, IMessageHandler>> HandlerResolvers
        = new Dictionary<Aegis.Protocol.MessageType, Func<IServiceProvider, IMessageHandler>>
    {
            [Aegis.Protocol.MessageType.MessageReadReceipt] = sp => sp.GetRequiredService<MessageReadReceiptHandler>(),
        [Aegis.Protocol.MessageType.Handshake] = sp => sp.GetRequiredService<HandshakeHandler>(),
        [Aegis.Protocol.MessageType.Auth] = sp => sp.GetRequiredService<AuthHandler>(),
        [Aegis.Protocol.MessageType.Ping] = sp => sp.GetRequiredService<PingHandler>(),
        [Aegis.Protocol.MessageType.Message] = sp => sp.GetRequiredService<MessageHandler>(),
        [Aegis.Protocol.MessageType.Ack] = sp => sp.GetRequiredService<AckHandler>(),
        [Aegis.Protocol.MessageType.Nack] = sp => sp.GetRequiredService<NackHandler>(),
        [Aegis.Protocol.MessageType.RetransmitRequest] = sp => sp.GetRequiredService<RetransmitRequestHandler>(),
        [Aegis.Protocol.MessageType.Register] = sp => sp.GetRequiredService<RegistrationHandler>(),
        [Aegis.Protocol.MessageType.UserPresence] = sp => sp.GetRequiredService<UserPresenceHandler>(),
        [Aegis.Protocol.MessageType.UserSearch] = sp => sp.GetRequiredService<UserSearchHandler>(),
        [Aegis.Protocol.MessageType.ChannelMessage] = sp => sp.GetRequiredService<ChannelMessageHandler>(),
        [Aegis.Protocol.MessageType.ChannelCreate] = sp => sp.GetRequiredService<ChannelCreateHandler>(),
        [Aegis.Protocol.MessageType.ChannelJoin] = sp => sp.GetRequiredService<ChannelJoinHandler>(),
        [Aegis.Protocol.MessageType.PrivateChatMessage] = sp => sp.GetRequiredService<PrivateChatMessageHandler>(),
        [Aegis.Protocol.MessageType.ChatListRequest] = sp => sp.GetRequiredService<ChatListHandler>(),
        [Aegis.Protocol.MessageType.PrivateChatHistoryRequest] = sp => sp.GetRequiredService<PrivateChatHistoryHandler>(),
        [Aegis.Protocol.MessageType.ChannelHistoryRequest] = sp => sp.GetRequiredService<ChannelHistoryHandler>(),
        [Aegis.Protocol.MessageType.ProfileUpdate] = sp => sp.GetRequiredService<ProfileUpdateHandler>(),
        [Aegis.Protocol.MessageType.ProfileGet] = sp => sp.GetRequiredService<ProfileGetHandler>(),
        [Aegis.Protocol.MessageType.ProfileAvatarAdd] = sp => sp.GetRequiredService<ProfileAvatarAddHandler>(),
        [Aegis.Protocol.MessageType.ProfileAvatarList] = sp => sp.GetRequiredService<ProfileAvatarListHandler>(),
        [Aegis.Protocol.MessageType.ProfileAvatarDelete] = sp => sp.GetRequiredService<ProfileAvatarDeleteHandler>(),
        [Aegis.Protocol.MessageType.ProfileAvatarSetPrimary] = sp => sp.GetRequiredService<ProfileAvatarSetPrimaryHandler>(),
        [Aegis.Protocol.MessageType.ChannelLinkUpdate] = sp => sp.GetRequiredService<ChannelLinkUpdateHandler>(),
        [Aegis.Protocol.MessageType.ChannelLinkGet] = sp => sp.GetRequiredService<ChannelLinkGetHandler>(),
        [Aegis.Protocol.MessageType.ChannelResolve] = sp => sp.GetRequiredService<ChannelResolveHandler>(),
        [Aegis.Protocol.MessageType.ChannelJoinByLink] = sp => sp.GetRequiredService<ChannelJoinByLinkHandler>(),
        [Aegis.Protocol.MessageType.MessageEdit] = sp => sp.GetRequiredService<MessageEditHandler>(),
        [Aegis.Protocol.MessageType.MessageDelete] = sp => sp.GetRequiredService<MessageDeleteHandler>(),
        [Aegis.Protocol.MessageType.ChannelEdit] = sp => sp.GetRequiredService<ChannelEditHandler>(),
        [Aegis.Protocol.MessageType.GroupCreate] = sp => sp.GetRequiredService<GroupCreateHandler>(),
        [Aegis.Protocol.MessageType.GroupEdit] = sp => sp.GetRequiredService<GroupEditHandler>(),
        [Aegis.Protocol.MessageType.GroupMessageSend] = sp => sp.GetRequiredService<GroupMessageSendHandler>(),
        [Aegis.Protocol.MessageType.MemberRoleUpdate] = sp => sp.GetRequiredService<MemberRoleUpdateHandler>(),
        [Aegis.Protocol.MessageType.MemberPermissionUpdate] = sp => sp.GetRequiredService<MemberPermissionUpdateHandler>(),
        [Aegis.Protocol.MessageType.MessageReadReceipt] = sp => sp.GetRequiredService<MessageReadReceiptHandler>(),
        [Aegis.Protocol.MessageType.MessageDeliveryReceipt] = sp => sp.GetRequiredService<MessageDeliveryReceiptHandler>(),
        // SERVER-002
        [Aegis.Protocol.MessageType.GroupHistoryRequest] = sp => sp.GetRequiredService<GroupHistoryHandler>(),
        // SERVER-003
        [Aegis.Protocol.MessageType.ChannelMembersRequest] = sp => sp.GetRequiredService<ChannelMembersHandler>(),
        [Aegis.Protocol.MessageType.GroupMembersRequest] = sp => sp.GetRequiredService<GroupMembersHandler>(),
        // SERVER-004
        [Aegis.Protocol.MessageType.ChannelLeave] = sp => sp.GetRequiredService<ChannelLeaveHandler>(),
        [Aegis.Protocol.MessageType.GroupLeave] = sp => sp.GetRequiredService<GroupLeaveHandler>(),
        // SERVER-005
        [Aegis.Protocol.MessageType.MessageReact] = sp => sp.GetRequiredService<MessageReactHandler>(),
        [Aegis.Protocol.MessageType.MessagePin] = sp => sp.GetRequiredService<MessagePinHandler>(),
        // SERVER-006
        [Aegis.Protocol.MessageType.RoomSettingsGet] = sp => sp.GetRequiredService<RoomSettingsGetHandler>(),
        [Aegis.Protocol.MessageType.RoomSettingsUpdate] = sp => sp.GetRequiredService<RoomSettingsUpdateHandler>(),
    };

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
        if (!ProtocolSafetyFacade.IsRoutableInboundType((ushort)type))
        {
            return null;
        }

        if (_handlers != null && _handlers.TryGetValue(type, out var directHandler))
        {
            return directHandler;
        }

        if (_serviceProvider == null)
        {
            return null;
        }

        if (HandlerResolvers.TryGetValue(type, out var resolver))
        {
            return resolver(_serviceProvider);
        }

        return null;
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
