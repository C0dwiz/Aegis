using Aegis.Protocol;
using Aegis.Transport;
using Aegis.Common.Logging;
using System.Text.Json;
using Aegis.Common;

namespace Aegis.Handlers;

public class AuthRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class AuthResponse
{
    public bool Success { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class AuthHandler : IMessageHandler
{
    private readonly IAntiSpamClient _antiSpam;
    private readonly IMessageSender _messageSender;
    private readonly Aegis.Crypto.ICryptoProvider _cryptoProvider;
    private readonly ILogger _logger;
    private readonly Dictionary<string, string> _users; // Simple in-memory user store
    
    public Aegis.Protocol.MessageType Type => Aegis.Protocol.MessageType.Auth;
    
    public AuthHandler(IAntiSpamClient antiSpam, IMessageSender messageSender, Aegis.Crypto.ICryptoProvider cryptoProvider, ILogger? logger = null)
    {
        _antiSpam = antiSpam;
        _messageSender = messageSender;
        _cryptoProvider = cryptoProvider;
        _logger = logger ?? new Aegis.Transport.NullLogger();
        
        // Initialize with some test users (in production, use a proper database)
        _users = new Dictionary<string, string>
        {
            ["admin"] = "admin123",
            ["user1"] = "password1",
            ["user2"] = "password2"
        };
    }
    
    public async ValueTask HandleAsync(ConnectionContext context, Message message)
    {
        await _antiSpam.CheckMessageAsync(context.ConnectionId, message.Payload);
        
        try
        {
            // Implement authentication logic
            var authRequest = JsonSerializer.Deserialize<AuthRequest>(message.Payload.ToArray());
            if (authRequest == null)
            {
                await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
                {
                    Success = false,
                    Error = "Invalid authentication request format"
                });
                return;
            }
            
            _logger.Info($"Authentication attempt for user: {authRequest.Username} from connection {context.ConnectionId}");
            
            // Check credentials
            var isAuthenticated = await AuthenticateUserAsync(authRequest);
            
            var response = new AuthResponse
            {
                Success = isAuthenticated,
                UserId = isAuthenticated ? authRequest.Username : string.Empty,
                SessionToken = isAuthenticated ? GenerateSessionToken(authRequest.Username) : string.Empty,
                Error = isAuthenticated ? string.Empty : "Invalid credentials"
            };
            
            if (isAuthenticated)
            {
                _logger.Info($"User {authRequest.Username} authenticated successfully from connection {context.ConnectionId}");
                
                // Store user info in connection context (in production, use proper session management)
                // This could be extended to store user roles, permissions, etc.
            }
            else
            {
                _logger.Warning($"Authentication failed for user {authRequest.Username} from connection {context.ConnectionId}");
            }
            
            await SendAuthResponseAsync(context, message.SequenceId, response);
        }
        catch (JsonException ex)
        {
            _logger.Error($"Invalid JSON in auth message from connection {context.ConnectionId}", ex);
            await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
            {
                Success = false,
                Error = "Invalid JSON format"
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"Error processing authentication from connection {context.ConnectionId}", ex);
            await SendAuthResponseAsync(context, message.SequenceId, new AuthResponse
            {
                Success = false,
                Error = "Internal server error"
            });
        }
    }
    
    private async Task<bool> AuthenticateUserAsync(AuthRequest request)
    {
        // Simple authentication logic (in production, use proper password hashing, database, etc.)
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            return false;
        
        // Check if user exists and password matches
        if (_users.TryGetValue(request.Username, out var storedPassword))
        {
            return storedPassword == request.Password; // In production, use proper password verification
        }
        
        // Could also support token-based authentication
        if (!string.IsNullOrEmpty(request.Token))
        {
            return await ValidateTokenAsync(request.Token);
        }
        
        return false;
    }
    
    private async Task<bool> ValidateTokenAsync(string token)
    {
        // Simple token validation (in production, use JWT or proper token validation)
        await Task.Delay(1); // Simulate async validation
        return token.StartsWith("valid_token_"); // Simple validation logic
    }
    
    private string GenerateSessionToken(string username)
    {
        // Generate a simple session token (in production, use proper token generation)
        return $"session_token_{username}_{DateTime.UtcNow:yyyyMMddHHmmss}";
    }
    
    private async Task SendAuthResponseAsync(ConnectionContext context, ulong sequenceId, AuthResponse response)
    {
        try
        {
            var responseJson = JsonSerializer.SerializeToUtf8Bytes(response);
            
            var ackMessage = new Message
            {
                Magic = ProtocolConstants.Magic,
                VersionMajor = ProtocolConstants.VersionMajor,
                VersionMinor = ProtocolConstants.VersionMinor,
                Type = Aegis.Protocol.MessageType.Ack,
                SequenceId = sequenceId,
                PayloadLength = (uint)responseJson.Length,
                Payload = responseJson,
                Mac = new byte[ProtocolConstants.MacSize]
            };
            
            // Encrypt and send through message sender
            var sessionKey = new byte[32]; // TODO: Get from session manager
            var encryptedMessage = await _cryptoProvider.EncryptMessageAsync(ackMessage, sessionKey);
            await _messageSender.SendMessageAsync(context.ConnectionId, encryptedMessage);
            
            _logger.Debug($"Authentication response sent to connection {context.ConnectionId}, success: {response.Success}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error sending authentication response to connection {context.ConnectionId}", ex);
            throw;
        }
    }
}
