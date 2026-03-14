using Aegis.Protocol;

namespace Aegis.Data.Policies;

public static class MediaPolicy
{
    public const int MaxAttachmentsPerMessage = 10;

    // Protocol-aware limits for payloads that are serialized as JSON with base64 content.
    // Max payload is ~1MB on wire, so limits are intentionally conservative.
    public const int MaxSingleAttachmentBytes = 512 * 1024; // 512KB
    public const int MaxTotalAttachmentsBytes = 700 * 1024; // 700KB

    public static int ProtocolPayloadBudgetBytes => ProtocolConstants.MaxPayloadSize;
}
