using Prometheus;

namespace Aegis.Handlers;

/// <summary>
/// Prometheus metric definitions for the Handlers layer
/// (authentication, handshake, spam filter, offline delivery).
/// </summary>
internal static class HandlerMetrics
{
    internal static readonly Counter AuthAttemptsTotal = Metrics
        .CreateCounter(
            "aegis_auth_attempts_total",
            "Total authentication attempts",
            new CounterConfiguration { LabelNames = ["result"] });
            // result labels: success | failure_credentials | failure_email | failure_2fa_required | failure_2fa_invalid | failure_no_handshake | failure_token

    internal static readonly Gauge AuthenticatedSessions = Metrics
        .CreateGauge(
            "aegis_authenticated_sessions",
            "Number of currently authenticated TCP sessions");

    internal static readonly Counter HandshakesTotal = Metrics
        .CreateCounter(
            "aegis_handshakes_total",
            "Total handshake V2 hello messages received",
            new CounterConfiguration { LabelNames = ["result"] });
            // result labels: success | failure

    internal static readonly Counter SpamMessagesBlockedTotal = Metrics
        .CreateCounter(
            "aegis_spam_messages_blocked_total",
            "Total messages rejected by the anti-spam filter");

    internal static readonly Counter OfflineMessagesDeliveredTotal = Metrics
        .CreateCounter(
            "aegis_offline_messages_delivered_total",
            "Total offline-queued messages delivered on reconnect");
}
