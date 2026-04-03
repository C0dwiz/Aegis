using Aegis.Handlers;
using Aegis.Protocol;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
	options.SwaggerDoc("v1", new OpenApiInfo
	{
		Title = "Aegis Protocol API",
		Version = "v1",
		Description = "Standalone HTTP API for Aegis protocol/OpenAPI documentation and migration notes."
	});
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
	options.SwaggerEndpoint("/swagger/v1/swagger.json", "Aegis Protocol API v1");
	options.RoutePrefix = "swagger";
});

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/health", () => Results.Ok(new { ok = true, service = "aegis-api" }));

app.MapGet("/api/protocol/message-types", () =>
	TypedResults.Ok(ProtocolCatalog.GetMessageTypes()))
	.WithName("GetProtocolMessageTypes")
	.WithSummary("Get all protocol MessageType values")
	.WithDescription("Returns every protocol message type with numeric code, category and short description.");

app.MapGet("/api/protocol/messages/{messageType}", (string messageType) =>
{
	if (!ProtocolCatalog.TryGetMessageDefinition(messageType, out var definition))
	{
		return Results.NotFound(new
		{
			error = "Unknown message type",
			value = messageType
		});
	}

	return Results.Ok(definition);
})
	.WithName("GetProtocolMessageDefinition")
	.WithSummary("Get message structure by type")
	.WithDescription("Accepts enum name (e.g. `FileTransfer`) or numeric id (e.g. `89`) and returns detailed payload fields and notes.");

app.MapGet("/api/protocol/exchanges", () =>
	TypedResults.Ok(ProtocolCatalog.GetExchangeExamples()))
	.WithName("GetProtocolExchanges")
	.WithSummary("Get protocol exchange examples")
	.WithDescription("Returns practical multi-step protocol exchanges (handshake, auth, direct chat with ack, file transfer). ");

app.MapGet("/api/protocol/errors", () =>
	TypedResults.Ok(ProtocolCatalog.GetErrorReference()))
	.WithName("GetProtocolErrors")
	.WithSummary("Get protocol error/status reference")
	.WithDescription("Returns ACK/NACK statuses, common error messages, and recommended recovery strategies.");

app.MapGet("/api/protocol/migration/v1-to-v2", () =>
	TypedResults.Ok(ProtocolCatalog.GetMigrationGuide()))
	.WithName("GetV1ToV2MigrationGuide")
	.WithSummary("Get migration guide from v1 to v2")
	.WithDescription("Checklist and compatibility steps for clients migrating to strict V2 handshake and newer payloads.");

app.Run();

internal static class ProtocolCatalog
{
	private static readonly IReadOnlyDictionary<MessageType, string> TypeCategories = new Dictionary<MessageType, string>
	{
		[MessageType.Auth] = "Auth",
		[MessageType.Handshake] = "Auth",
		[MessageType.Register] = "Auth",
		[MessageType.RegisterResponse] = "Auth",
		[MessageType.Message] = "Messaging",
		[MessageType.Ack] = "Transport",
		[MessageType.Error] = "Transport",
		[MessageType.UserTyping] = "Realtime",
		[MessageType.UserTypingEvent] = "Realtime",
		[MessageType.FileTransfer] = "Media",
		[MessageType.FileTransferResponse] = "Media",
		[MessageType.FileTransferChunk] = "Media"
	};

	private static readonly IReadOnlyDictionary<MessageType, MessageDefinition> DetailedDefinitions =
		new Dictionary<MessageType, MessageDefinition>
		{
			[MessageType.Handshake] = new(
				MessageType.Handshake,
				"Auth",
				"V2 staged handshake envelope.",
				[
					new("stage", "string", true, "Handshake stage: client_hello_v2 or client_finish_v2."),
					new("clientHello.apiId", "int", false, "Official app id for credential-aware handshake."),
					new("clientHello.appHash", "string", false, "Official app hash.") ,
					new("clientHello.clientEphemeralPublicKey", "bytes/base64", true, "Client ephemeral ECDH key."),
					new("clientHello.clientNonce", "bytes/base64", true, "Random nonce."),
					new("clientHello.timestampUtc", "DateTime", true, "Client UTC timestamp."),
					new("clientFinish.cookie", "bytes/base64", true, "Server-issued cookie echoed by client."),
					new("clientFinish.proof", "bytes/base64", true, "Client finish MAC proof.")
				],
				"When strict V2 is enabled, legacy payload is rejected.") ,

			[MessageType.Auth] = new(
				MessageType.Auth,
				"Auth",
				"Username/password auth plus optional 2FA proof.",
				[
					new("username", "string", true, "Login identifier."),
					new("password", "string", true, "User password."),
					new("totpCode", "string", false, "Required if account has 2FA enabled.")
				],
				"Authentication requires a session with established handshake."),

			[MessageType.Message] = new(
				MessageType.Message,
				"Messaging",
				"Direct/private message send request.",
				[
					new("recipientId", "ulong", true, "Recipient user id."),
					new("content", "string", true, "Message text or normalized media payload."),
					new("parseMode", "string", false, "Formatting mode (optional)."),
					new("signalV3", "object", false, "Optional SignalV3 envelope metadata.")
				],
				"On success server emits Ack and may push PrivateChatMessage event to recipient."),

			[MessageType.GroupCreate] = new(
				MessageType.GroupCreate,
				"Groups",
				"Create group request.",
				[
					new("name", "string", true, "Group name; minimum length 2."),
					new("description", "string", false, "Optional group description.")
				],
				"Creator is automatically added as owner."),

			[MessageType.UserTyping] = new(
				MessageType.UserTyping,
				"Realtime",
				"Typing indicator request.",
				[
					new("scope", "string", true, "private/group/channel."),
					new("targetId", "ulong", true, "Target room or peer identifier."),
					new("isTyping", "bool", true, "True when user starts typing, false on stop."),
					new("toUserId", "ulong", false, "Used for private scope only.")
				],
				"Broadcast is throttled to minimum 500ms between typing=true updates."),

			[MessageType.FileTransfer] = new(
				MessageType.FileTransfer,
				"Media",
				"File upload/download command envelope.",
				[
					new("action", "string", true, "init | chunk | complete | download."),
					new("transferId", "string", false, "Required for chunk/complete."),
					new("fileId", "string", false, "Required for download."),
					new("fileName", "string", false, "Required for init."),
					new("mimeType", "string", false, "Defaults to application/octet-stream."),
					new("totalSize", "long", false, "Required for init; max 100MB."),
					new("totalChunks", "int", false, "Required for init."),
					new("chunkIndex", "int", false, "Required for chunk."),
					new("chunkDataBase64", "string", false, "Required for chunk."),
					new("allowedUserIds", "ulong[]", false, "Optional ACL for downloads.")
				],
				"Download sends FileTransferChunk events after FileTransferResponse(started).")
		};

	public static IReadOnlyList<MessageTypeInfo> GetMessageTypes()
	{
		return Enum.GetValues<MessageType>()
			.OrderBy(v => (ushort)v)
			.Select(v => new MessageTypeInfo(
				Name: v.ToString(),
				Code: (ushort)v,
				Category: TypeCategories.TryGetValue(v, out var category) ? category : "General",
				Description: GetDescription(v)))
			.ToList();
	}

	public static bool TryGetMessageDefinition(string messageType, out MessageDefinition definition)
	{
		definition = null!;

		if (TryParseMessageType(messageType, out var parsed) == false)
		{
			return false;
		}

		if (DetailedDefinitions.TryGetValue(parsed, out var specific))
		{
			definition = specific;
			return true;
		}

		definition = new MessageDefinition(
			parsed,
			TypeCategories.TryGetValue(parsed, out var category) ? category : "General",
			GetDescription(parsed),
			[
				new("payload", "object", false, "Message-specific payload. See protocol.md/wire-spec.md for canonical schema."),
				new("sequenceId", "ulong", true, "Protocol sequence number."),
				new("flags", "byte", false, "MessageFlags bitmask.")
			],
			"This message type is documented at catalog level; detailed payload contract is protocol-specific.");

		return true;
	}

	public static IReadOnlyList<ExchangeExample> GetExchangeExamples()
	{
		return
		[
			new ExchangeExample(
				"V2 Handshake",
				"Strict V2 multi-stage handshake with cookie and proof.",
				[
					new("Client -> Server", "Handshake(client_hello_v2)", "ephemeral key + nonce + timestamp + optional app credentials"),
					new("Server -> Client", "Handshake(server_hello_v2)", "server key + cookie + transcript data"),
					new("Client -> Server", "Handshake(client_finish_v2)", "cookie echo + proof MAC"),
					new("Server -> Client", "HandshakeResponse(success)", "session key is established")
				]),

			new ExchangeExample(
				"Auth Flow",
				"Auth after successful handshake.",
				[
					new("Client -> Server", "Auth", "username/password (+ optional totpCode)"),
					new("Server -> Client", "AuthResponse", "success + user/session metadata")
				]),

			new ExchangeExample(
				"Direct Message with ACK",
				"Private send and transport acknowledgment.",
				[
					new("Client -> Server", "Message", "recipientId + content"),
					new("Server -> Sender", "Ack", "sequence acknowledged"),
					new("Server -> Recipient", "PrivateChatMessageEvent", "new message event payload")
				]),

			new ExchangeExample(
				"File Transfer Upload + Download",
				"Chunked media flow with ACL and download chunks.",
				[
					new("Client -> Server", "FileTransfer(action=init)", "declares metadata and total chunks"),
					new("Server -> Client", "FileTransferResponse", "returns transferId"),
					new("Client -> Server", "FileTransfer(action=chunk)", "sends base64 chunks in order"),
					new("Client -> Server", "FileTransfer(action=complete)", "finalizes upload and returns fileId"),
					new("Client -> Server", "FileTransfer(action=download)", "requests file by fileId"),
					new("Server -> Client", "FileTransferChunk", "stream of chunk payloads")
				])
		];
	}

	public static ErrorReference GetErrorReference()
	{
		return new ErrorReference(
			AckStatuses:
			[
				new StatusCodeRef("Ok", (byte)AckStatus.Ok, "Message processed successfully."),
				new StatusCodeRef("Error", (byte)AckStatus.Error, "Message processing failed."),
				new StatusCodeRef("Retry", (byte)AckStatus.Retry, "Client should retry message."),
				new StatusCodeRef("NotImplemented", (byte)AckStatus.NotImplemented, "Message type is not supported.")
			],
			CommonErrors:
			[
				new ErrorRef("V2 handshake required", "Server is configured for strict V2 handshake.", "Upgrade client to V2 staged handshake."),
				new ErrorRef("Not authenticated", "Message requires authenticated session.", "Run handshake + auth flow before calling restricted messages."),
				new ErrorRef("Message rejected by anti-spam", "Anti-spam policy blocked message.", "Backoff and retry with lower send rate/content change."),
				new ErrorRef("File exceeds 100MB limit", "Upload validation failure.", "Split file or use external storage reference.")
			]);
	}

	public static MigrationGuide GetMigrationGuide()
	{
		return new MigrationGuide(
			Summary: "Migration checklist from pre-V2 clients to current protocol runtime.",
			Steps:
			[
				"Enable V2 handshake implementation (client_hello_v2 / client_finish_v2).",
				"Stop relying on legacy handshake fallback when server uses strict V2.",
				"Support MessageType 87..91 for typing and file transfer features.",
				"Handle compatibility payload aliases: Id/MessageId and CreatedAt/CreatedAtUtc.",
				"Treat SignalV3 envelope as optional metadata in private message payloads.",
				"Re-run smoke tests for auth, direct messaging, group/chat sync, and file transfer."
			],
			ValidationCommand: "dotnet test tests/Aegis.Tests/Aegis.Tests.csproj -c Debug");
	}

	private static string GetDescription(MessageType type)
	{
		return type switch
		{
			MessageType.Auth => "Authenticate user session.",
			MessageType.Handshake => "Negotiate session key and handshake state.",
			MessageType.Message => "Send direct/private message.",
			MessageType.Ack => "Delivery acknowledgment.",
			MessageType.Error => "Error response payload.",
			MessageType.GroupCreate => "Create group room.",
			MessageType.UserTyping => "Typing update request.",
			MessageType.UserTypingEvent => "Typing update event.",
			MessageType.FileTransfer => "File transfer command.",
			MessageType.FileTransferResponse => "File transfer command response.",
			MessageType.FileTransferChunk => "Chunk frame for downloads.",
			_ => "Protocol message.",
		};
	}

	private static bool TryParseMessageType(string value, out MessageType type)
	{
		if (Enum.TryParse<MessageType>(value, ignoreCase: true, out type))
		{
			return true;
		}

		if (ushort.TryParse(value, out var numeric) && Enum.IsDefined(typeof(MessageType), numeric))
		{
			type = (MessageType)numeric;
			return true;
		}

		type = MessageType.Unknown;
		return false;
	}
}

public sealed record MessageTypeInfo(string Name, ushort Code, string Category, string Description);

public sealed record MessageDefinition(
	MessageType MessageType,
	string Category,
	string Summary,
	IReadOnlyList<FieldDefinition> Fields,
	string Notes);

public sealed record FieldDefinition(string Name, string Type, bool Required, string Description);

public sealed record ExchangeExample(string Title, string Summary, IReadOnlyList<ExchangeStep> Steps);

public sealed record ExchangeStep(string Direction, string MessageType, string Details);

public sealed record ErrorReference(IReadOnlyList<StatusCodeRef> AckStatuses, IReadOnlyList<ErrorRef> CommonErrors);

public sealed record StatusCodeRef(string Name, byte Code, string Meaning);

public sealed record ErrorRef(string Error, string Cause, string Recovery);

public sealed record MigrationGuide(string Summary, IReadOnlyList<string> Steps, string ValidationCommand);
