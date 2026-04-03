using System.Collections.Concurrent;
using System.Text.Json;
using Aegis.Common;
using Aegis.Data.Repositories;
using Aegis.Protocol;
using Aegis.Transport;
using Microsoft.Extensions.Logging;

namespace Aegis.Handlers;

public record UserTypingRequest(
    string Scope,
    ulong TargetId,
    bool IsTyping,
    ulong? ToUserId = null
);

public record UserTypingEvent(
    string Scope,
    ulong TargetId,
    ulong UserId,
    bool IsTyping,
    DateTime TimestampUtc
);

public sealed class TypingIndicatorStore : IDisposable
{
    private static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MinBroadcastInterval = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<string, DateTime> _activeStates = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastBroadcastByKey = new();
    private readonly Timer _cleanupTimer;

    public TypingIndicatorStore()
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public bool ShouldBroadcast(ulong userId, string scope, ulong targetId, bool isTyping)
    {
        var stateKey = $"{userId}:{scope}:{targetId}";
        var now = DateTime.UtcNow;

        if (isTyping)
        {
            if (_lastBroadcastByKey.TryGetValue(stateKey, out var last) && now - last < MinBroadcastInterval)
            {
                return false;
            }

            _activeStates[stateKey] = now;
            _lastBroadcastByKey[stateKey] = now;
            return true;
        }

        _activeStates.TryRemove(stateKey, out _);
        _lastBroadcastByKey[stateKey] = now;
        return true;
    }

    private void Cleanup()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _activeStates)
        {
            if (now - pair.Value > TypingTtl)
            {
                _activeStates.TryRemove(pair.Key, out _);
            }
        }

        foreach (var pair in _lastBroadcastByKey)
        {
            if (now - pair.Value > TimeSpan.FromMinutes(5))
            {
                _lastBroadcastByKey.TryRemove(pair.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

public class UserTypingHandler : IMessageHandler
{
    public MessageType Type => MessageType.UserTyping;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly TypingIndicatorStore _typingStore;
    private readonly IGroupRepository _groupRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly ILogger<UserTypingHandler> _logger;

    public UserTypingHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        TypingIndicatorStore typingStore,
        IGroupRepository groupRepository,
        IChannelRepository channelRepository,
        ILogger<UserTypingHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _typingStore = typingStore;
        _groupRepository = groupRepository;
        _channelRepository = channelRepository;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                return;
            }

            var request = PayloadSerializer.Deserialize<UserTypingRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.Scope) || request.TargetId == 0)
            {
                return;
            }

            var scope = request.Scope.Trim().ToLowerInvariant();
            if (!_typingStore.ShouldBroadcast(session.UserId, scope, request.TargetId, request.IsTyping))
            {
                return;
            }

            var eventPayload = PayloadSerializer.Serialize(new UserTypingEvent(
                scope,
                request.TargetId,
                session.UserId,
                request.IsTyping,
                DateTime.UtcNow));

            if (scope == "private" && request.ToUserId.HasValue && request.ToUserId.Value != 0)
            {
                await SendToUserAsync(request.ToUserId.Value, eventPayload);
                return;
            }

            if (scope == "group")
            {
                var members = await _groupRepository.GetGroupMembersAsync(request.TargetId);
                foreach (var member in members.Where(m => m.IsActive && m.UserId != session.UserId))
                {
                    await SendToUserAsync(member.UserId, eventPayload);
                }

                return;
            }

            if (scope == "channel")
            {
                var members = await _channelRepository.GetChannelMembersAsync(request.TargetId);
                foreach (var member in members.Where(m => m.IsActive && m.UserId != session.UserId))
                {
                    await SendToUserAsync(member.UserId, eventPayload);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process typing event from connection {ConnectionId}", context.ConnectionId);
        }
    }

    private async Task SendToUserAsync(ulong userId, byte[] payload)
    {
        if (!_sessionManager.TryGetConnectionIdByUserId(userId, out var connectionId))
        {
            return;
        }

        await _messageSender.SendProtocolMessageAsync(
            connectionId,
            (ushort)MessageType.UserTypingEvent,
            0,
            payload,
            allowUnsigned: false);
    }
}

public record FileTransferRequest(
    string Action,
    string? TransferId = null,
    string? FileId = null,
    string? FileName = null,
    string? MimeType = null,
    long? TotalSize = null,
    int? TotalChunks = null,
    int? ChunkIndex = null,
    string? ChunkDataBase64 = null,
    IReadOnlyList<ulong>? AllowedUserIds = null
);

public record FileTransferResponse(
    bool Success,
    string? Message = null,
    string? TransferId = null,
    string? FileId = null,
    int? ChunkIndex = null,
    int? TotalChunks = null,
    string? ChunkDataBase64 = null,
    string? FileName = null,
    string? MimeType = null,
    long? TotalSize = null
);

internal sealed class FileTransferMetadata
{
    public string FileId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/octet-stream";
    public long TotalSize { get; init; }
    public ulong UploaderUserId { get; init; }
    public List<ulong> AllowedUserIds { get; init; } = new();
}

internal sealed class FileUploadSession
{
    public string TransferId { get; init; } = string.Empty;
    public string TempPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/octet-stream";
    public long TotalSize { get; init; }
    public int TotalChunks { get; init; }
    public ulong UploaderUserId { get; init; }
    public List<ulong> AllowedUserIds { get; init; } = new();
    public int LastChunkIndex { get; set; } = -1;
    public long BytesWritten { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public interface IFileDownloadRateLimiter
{
    Task WaitForBudgetAsync(ulong connectionId, int bytesToSend, CancellationToken cancellationToken = default);
}

public sealed class FileDownloadRateLimiter : IFileDownloadRateLimiter
{
    private sealed class DownloadBudget
    {
        public long WindowStartTicks;
        public int BytesSentInWindow;
        public readonly object Sync = new();
    }

    private readonly ConcurrentDictionary<ulong, DownloadBudget> _budgets = new();
    private readonly int _bytesPerSecond;

    public FileDownloadRateLimiter(int bytesPerSecond = 2 * 1024 * 1024)
    {
        _bytesPerSecond = Math.Max(64 * 1024, bytesPerSecond);
    }

    public async Task WaitForBudgetAsync(ulong connectionId, int bytesToSend, CancellationToken cancellationToken = default)
    {
        if (bytesToSend <= 0)
        {
            return;
        }

        var budget = _budgets.GetOrAdd(connectionId, _ => new DownloadBudget
        {
            WindowStartTicks = DateTime.UtcNow.Ticks,
            BytesSentInWindow = 0
        });

        TimeSpan? delay = null;
        lock (budget.Sync)
        {
            var now = DateTime.UtcNow;
            var windowStart = new DateTime(Volatile.Read(ref budget.WindowStartTicks), DateTimeKind.Utc);
            if (now - windowStart >= TimeSpan.FromSeconds(1))
            {
                budget.WindowStartTicks = now.Ticks;
                budget.BytesSentInWindow = 0;
            }

            var projected = budget.BytesSentInWindow + bytesToSend;
            if (projected > _bytesPerSecond)
            {
                delay = TimeSpan.FromSeconds(1) - (now - windowStart);
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                budget.WindowStartTicks = now.Add(delay.Value).Ticks;
                budget.BytesSentInWindow = bytesToSend;
            }
            else
            {
                budget.BytesSentInWindow = projected;
            }
        }

        if (delay.HasValue && delay.Value > TimeSpan.Zero)
        {
            await Task.Delay(delay.Value, cancellationToken);
        }
    }
}

public sealed class FileTransferStore : IDisposable
{
    public const long MaxFileBytes = 100L * 1024 * 1024;
    private static readonly TimeSpan UploadSessionTtl = TimeSpan.FromMinutes(15);

    private readonly string _rootDir;
    private readonly string _uploadsDir;
    private readonly string _metaDir;
    private readonly ConcurrentDictionary<string, FileUploadSession> _sessions = new();
    private readonly Timer _cleanupTimer;

    public FileTransferStore()
    {
        _rootDir = Path.Combine(AppContext.BaseDirectory, "data", "file-transfer");
        _uploadsDir = Path.Combine(_rootDir, "uploads");
        _metaDir = Path.Combine(_rootDir, "meta");

        Directory.CreateDirectory(_uploadsDir);
        Directory.CreateDirectory(_metaDir);

        _cleanupTimer = new Timer(_ => CleanupExpiredSessions(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    internal FileUploadSession CreateSession(ulong uploaderUserId, string fileName, string mimeType, long totalSize, int totalChunks, IReadOnlyList<ulong>? allowedUserIds)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(_uploadsDir, $"{transferId}.part");

        var session = new FileUploadSession
        {
            TransferId = transferId,
            TempPath = tempPath,
            FileName = fileName,
            MimeType = mimeType,
            TotalSize = totalSize,
            TotalChunks = totalChunks,
            UploaderUserId = uploaderUserId,
            AllowedUserIds = allowedUserIds?.Distinct().ToList() ?? new List<ulong>()
        };

        _sessions[transferId] = session;
        return session;
    }

    internal bool TryGetSession(string transferId, out FileUploadSession session)
    {
        return _sessions.TryGetValue(transferId, out session!);
    }

    internal void Touch(string transferId)
    {
        if (_sessions.TryGetValue(transferId, out var session))
        {
            session.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    internal void RemoveSession(string transferId)
    {
        if (_sessions.TryRemove(transferId, out var session))
        {
            TryDeleteFile(session.TempPath);
        }
    }

    internal string SaveFinalizedUpload(FileUploadSession session)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var finalPath = Path.Combine(_uploadsDir, fileId + ".bin");
        File.Move(session.TempPath, finalPath, overwrite: true);

        var metadata = new FileTransferMetadata
        {
            FileId = fileId,
            FileName = session.FileName,
            MimeType = session.MimeType,
            TotalSize = session.TotalSize,
            UploaderUserId = session.UploaderUserId,
            AllowedUserIds = session.AllowedUserIds
        };

        var metaPath = Path.Combine(_metaDir, fileId + ".json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(metadata));
        _sessions.TryRemove(session.TransferId, out _);
        return fileId;
    }

    internal bool TryGetFileMetadata(string fileId, out FileTransferMetadata metadata, out string filePath)
    {
        metadata = null!;
        filePath = Path.Combine(_uploadsDir, fileId + ".bin");
        var metaPath = Path.Combine(_metaDir, fileId + ".json");

        if (!File.Exists(filePath) || !File.Exists(metaPath))
        {
            return false;
        }

        var json = File.ReadAllText(metaPath);
        var parsed = JsonSerializer.Deserialize<FileTransferMetadata>(json);
        if (parsed == null)
        {
            return false;
        }

        metadata = parsed;
        return true;
    }

    private void CleanupExpiredSessions()
    {
        var cutoff = DateTime.UtcNow - UploadSessionTtl;
        foreach (var pair in _sessions)
        {
            if (pair.Value.LastUpdatedUtc < cutoff)
            {
                if (_sessions.TryRemove(pair.Key, out var session))
                {
                    TryDeleteFile(session.TempPath);
                }
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup for expired temp uploads.
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

public class FileTransferHandler : IMessageHandler
{
    private const int DownloadChunkSizeBytes = 256 * 1024;
    public MessageType Type => MessageType.FileTransfer;

    private readonly SessionManager _sessionManager;
    private readonly IMessageSender _messageSender;
    private readonly FileTransferStore _fileStore;
    private readonly IFileDownloadRateLimiter _downloadRateLimiter;
    private readonly ILogger<FileTransferHandler> _logger;

    public FileTransferHandler(
        SessionManager sessionManager,
        IMessageSender messageSender,
        FileTransferStore fileStore,
        IFileDownloadRateLimiter downloadRateLimiter,
        ILogger<FileTransferHandler> logger)
    {
        _sessionManager = sessionManager;
        _messageSender = messageSender;
        _fileStore = fileStore;
        _downloadRateLimiter = downloadRateLimiter;
        _logger = logger;
    }

    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        try
        {
            var session = _sessionManager.GetAuthenticatedSession(context.ConnectionId);
            if (session == null)
            {
                await SendResponseAsync(context.ConnectionId, message.SequenceId, new FileTransferResponse(false, "Not authenticated"));
                return;
            }

            var request = PayloadSerializer.Deserialize<FileTransferRequest>(message.Payload);
            if (request == null || string.IsNullOrWhiteSpace(request.Action))
            {
                await SendResponseAsync(context.ConnectionId, message.SequenceId, new FileTransferResponse(false, "Invalid payload"));
                return;
            }

            var action = request.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "init":
                    await HandleInitAsync(context.ConnectionId, message.SequenceId, session.UserId, request);
                    break;
                case "chunk":
                    await HandleChunkAsync(context.ConnectionId, message.SequenceId, session.UserId, request);
                    break;
                case "complete":
                    await HandleCompleteAsync(context.ConnectionId, message.SequenceId, session.UserId, request);
                    break;
                case "download":
                    await HandleDownloadAsync(context.ConnectionId, message.SequenceId, session.UserId, request);
                    break;
                default:
                    await SendResponseAsync(context.ConnectionId, message.SequenceId, new FileTransferResponse(false, "Unsupported action"));
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "File transfer handling failed for connection {ConnectionId}", context.ConnectionId);
            await SendResponseAsync(context.ConnectionId, message.SequenceId, new FileTransferResponse(false, "Internal server error"));
        }
    }

    private async Task HandleInitAsync(ulong connectionId, ulong sequenceId, ulong userId, FileTransferRequest request)
    {
        var fileName = request.FileName?.Trim();
        var mimeType = request.MimeType?.Trim();

        if (string.IsNullOrWhiteSpace(fileName) || request.TotalSize is null || request.TotalSize <= 0 || request.TotalChunks is null || request.TotalChunks <= 0)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Missing file metadata"));
            return;
        }

        if (request.TotalSize > FileTransferStore.MaxFileBytes)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "File exceeds 100MB limit"));
            return;
        }

        var uploadSession = _fileStore.CreateSession(
            userId,
            fileName,
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
            request.TotalSize.Value,
            request.TotalChunks.Value,
            request.AllowedUserIds);

        await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(true, TransferId: uploadSession.TransferId));
    }

    private async Task HandleChunkAsync(ulong connectionId, ulong sequenceId, ulong userId, FileTransferRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TransferId) || request.ChunkIndex is null || string.IsNullOrWhiteSpace(request.ChunkDataBase64))
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Missing chunk payload"));
            return;
        }

        if (!_fileStore.TryGetSession(request.TransferId, out var uploadSession) || uploadSession.UploaderUserId != userId)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Upload session not found"));
            return;
        }

        if (request.ChunkIndex != uploadSession.LastChunkIndex + 1)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Chunk sequence mismatch"));
            return;
        }

        byte[] chunkBytes;
        try
        {
            chunkBytes = Convert.FromBase64String(request.ChunkDataBase64);
        }
        catch
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Chunk is not valid base64"));
            return;
        }

        uploadSession.BytesWritten += chunkBytes.Length;
        if (uploadSession.BytesWritten > uploadSession.TotalSize || uploadSession.BytesWritten > FileTransferStore.MaxFileBytes)
        {
            _fileStore.RemoveSession(uploadSession.TransferId);
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Upload exceeds declared file size"));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(uploadSession.TempPath)!);
        await using (var stream = new FileStream(uploadSession.TempPath, FileMode.Append, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await stream.WriteAsync(chunkBytes.AsMemory(0, chunkBytes.Length));
        }

        uploadSession.LastChunkIndex = request.ChunkIndex.Value;
        uploadSession.LastUpdatedUtc = DateTime.UtcNow;

        await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(true, ChunkIndex: request.ChunkIndex));
    }

    private async Task HandleCompleteAsync(ulong connectionId, ulong sequenceId, ulong userId, FileTransferRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TransferId))
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Missing transferId"));
            return;
        }

        if (!_fileStore.TryGetSession(request.TransferId, out var uploadSession) || uploadSession.UploaderUserId != userId)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Upload session not found"));
            return;
        }

        if (uploadSession.LastChunkIndex + 1 != uploadSession.TotalChunks || uploadSession.BytesWritten != uploadSession.TotalSize)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Upload is incomplete"));
            return;
        }

        var fileId = _fileStore.SaveFinalizedUpload(uploadSession);
        await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(true, FileId: fileId));
    }

    private async Task HandleDownloadAsync(ulong connectionId, ulong sequenceId, ulong userId, FileTransferRequest request)
    {
        var fileId = request.FileId?.Trim();
        if (string.IsNullOrWhiteSpace(fileId))
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Missing fileId"));
            return;
        }

        if (!_fileStore.TryGetFileMetadata(fileId, out var metadata, out var filePath))
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "File not found"));
            return;
        }

        var allowed = userId == metadata.UploaderUserId || metadata.AllowedUserIds.Contains(userId);
        if (!allowed)
        {
            await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(false, "Access denied"));
            return;
        }

        var totalChunks = (int)Math.Ceiling(metadata.TotalSize / (double)DownloadChunkSizeBytes);
        await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(
            true,
            Message: "Download started",
            FileId: fileId,
            TotalChunks: totalChunks,
            FileName: metadata.FileName,
            MimeType: metadata.MimeType,
            TotalSize: metadata.TotalSize));

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, DownloadChunkSizeBytes, useAsync: true);
        var buffer = new byte[DownloadChunkSizeBytes];
        var chunkIndex = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (read <= 0)
            {
                break;
            }

            var chunkData = Convert.ToBase64String(buffer, 0, read);
            var chunkPayload = PayloadSerializer.Serialize(new FileTransferResponse(
                true,
                FileId: fileId,
                ChunkIndex: chunkIndex,
                TotalChunks: totalChunks,
                ChunkDataBase64: chunkData));

            await _downloadRateLimiter.WaitForBudgetAsync(connectionId, read);

            await _messageSender.SendProtocolMessageAsync(
                connectionId,
                (ushort)MessageType.FileTransferChunk,
                0,
                chunkPayload,
                allowUnsigned: false);

            chunkIndex++;
        }

        await SendResponseAsync(connectionId, sequenceId, new FileTransferResponse(true, Message: "Download complete", FileId: fileId));
    }

    private async Task SendResponseAsync(ulong connectionId, ulong sequenceId, FileTransferResponse response)
    {
        var payload = PayloadSerializer.Serialize(response);
        await _messageSender.SendProtocolMessageAsync(
            connectionId,
            (ushort)MessageType.FileTransferResponse,
            sequenceId,
            payload,
            allowUnsigned: false);
    }
}
