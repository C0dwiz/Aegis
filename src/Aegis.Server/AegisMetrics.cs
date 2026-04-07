using Prometheus;

namespace Aegis.Server;

/// <summary>
/// Centralised Prometheus metric definitions for Aegis Messenger Server.
/// All counters/gauges/histograms are static singletons so any code can
/// record observations without dependency injection indirection.
/// </summary>
public static class AegisMetrics
{
    // ── Connections ──────────────────────────────────────────────────────────

    public static readonly Gauge ActiveConnections = Metrics
        .CreateGauge(
            "aegis_active_connections",
            "Number of currently open TCP connections");

    public static readonly Counter ConnectionsTotal = Metrics
        .CreateCounter(
            "aegis_connections_total",
            "Total TCP connections accepted since startup");

    public static readonly Counter ConnectionsRejectedTotal = Metrics
        .CreateCounter(
            "aegis_connections_rejected_total",
            "Total TCP connections rejected (rate-limit, balancer, etc.)",
            new CounterConfiguration { LabelNames = ["reason"] });

    // ── Messages ─────────────────────────────────────────────────────────────

    public static readonly Counter MessagesReceivedTotal = Metrics
        .CreateCounter(
            "aegis_messages_received_total",
            "Total protocol messages received from clients",
            new CounterConfiguration { LabelNames = ["message_type"] });

    public static readonly Counter MessagesSentTotal = Metrics
        .CreateCounter(
            "aegis_messages_sent_total",
            "Total protocol messages sent to clients",
            new CounterConfiguration { LabelNames = ["message_type"] });

    public static readonly Counter MessagesDroppedTotal = Metrics
        .CreateCounter(
            "aegis_messages_dropped_total",
            "Total messages dropped (malformed, replay, rate-limited, etc.)",
            new CounterConfiguration { LabelNames = ["reason"] });

    // ── Processing latency ───────────────────────────────────────────────────

    public static readonly Histogram MessageProcessingDuration = Metrics
        .CreateHistogram(
            "aegis_message_processing_duration_seconds",
            "End-to-end handler processing time per protocol message type",
            new HistogramConfiguration
            {
                LabelNames    = ["message_type"],
                Buckets       = Histogram.ExponentialBuckets(0.0005, 2, 14) // 0.5 ms → 4 s
            });

    // ── Authentication ───────────────────────────────────────────────────────

    public static readonly Counter AuthAttemptsTotal = Metrics
        .CreateCounter(
            "aegis_auth_attempts_total",
            "Total authentication attempts",
            new CounterConfiguration { LabelNames = ["result"] }); // success | failure_credentials | failure_2fa | …

    public static readonly Gauge AuthenticatedSessions = Metrics
        .CreateGauge(
            "aegis_authenticated_sessions",
            "Number of currently authenticated TCP sessions");

    // ── Handshake ────────────────────────────────────────────────────────────

    public static readonly Counter HandshakesTotal = Metrics
        .CreateCounter(
            "aegis_handshakes_total",
            "Total handshake hello messages received",
            new CounterConfiguration { LabelNames = ["result"] }); // success | failure

    // ── Rate limiter ─────────────────────────────────────────────────────────

    public static readonly Counter RateLimitHitsTotal = Metrics
        .CreateCounter(
            "aegis_rate_limit_hits_total",
            "Total rate-limit violations",
            new CounterConfiguration { LabelNames = ["scope"] }); // connection | auth | message

    // ── Offline queue ────────────────────────────────────────────────────────

    public static readonly Counter OfflineMessagesDeliveredTotal = Metrics
        .CreateCounter(
            "aegis_offline_messages_delivered_total",
            "Total offline-queued messages delivered on reconnect");

    public static readonly Counter OfflineMessagesDroppedTotal = Metrics
        .CreateCounter(
            "aegis_offline_messages_dropped_total",
            "Total offline-queued messages dropped (queue full)");

    // ── Health ───────────────────────────────────────────────────────────────

    public static readonly Gauge CpuLoadPercent = Metrics
        .CreateGauge(
            "aegis_cpu_load_percent",
            "Estimated CPU load of the server process (0–100)");

    public static readonly Gauge MemoryLoadPercent = Metrics
        .CreateGauge(
            "aegis_memory_load_percent",
            "Estimated managed heap memory usage in percent of GC budget");
}
