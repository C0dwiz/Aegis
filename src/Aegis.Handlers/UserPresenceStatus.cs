using Aegis.Common;

namespace Aegis.Handlers;

public static class UserPresenceStatus
{
    public const string Online = "online";
    public const string WasOnline = "was_online";
    public const string Recently = "recently";
    public const string LongAgo = "long_ago";
}

public sealed class UserPresenceResolver
{
    private static readonly TimeSpan WasOnlineWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecentlyWindow = TimeSpan.FromDays(7);

    private readonly SessionManager _sessionManager;

    public UserPresenceResolver(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public string Resolve(ulong userId, DateTime? lastSeenAt)
    {
        if (_sessionManager.IsUserOnline(userId))
        {
            return UserPresenceStatus.Online;
        }

        if (!lastSeenAt.HasValue)
        {
            return UserPresenceStatus.LongAgo;
        }

        var delta = DateTime.UtcNow - lastSeenAt.Value;
        if (delta <= WasOnlineWindow)
        {
            return UserPresenceStatus.WasOnline;
        }

        if (delta <= RecentlyWindow)
        {
            return UserPresenceStatus.Recently;
        }

        return UserPresenceStatus.LongAgo;
    }
}
