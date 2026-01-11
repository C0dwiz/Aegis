namespace Aegis.Handlers;

public interface IAntiSpamClient
{
    Task<bool> CheckMessageAsync(ulong connectionId, byte[] message);
}

public class AntiSpamRequest
{
    public ulong ConnectionId { get; set; }
    public string MessageHash { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}

public class AntiSpamResponse
{
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Score { get; set; }
    public string[]? MatchedRules { get; set; }
}

public class AntiSpamClient : IAntiSpamClient
{
    private readonly HttpClient _httpClient;
    private readonly string _serviceUrl;
    private readonly Dictionary<ulong, DateTime> _connectionLastMessage;
    private readonly Dictionary<ulong, int> _connectionMessageCount;
    private readonly object _lock = new object();
    
    public AntiSpamClient(string serviceUrl = "http://localhost:8080")
    {
        _serviceUrl = serviceUrl;
        _httpClient = new HttpClient();
        _connectionLastMessage = new Dictionary<ulong, DateTime>();
        _connectionMessageCount = new Dictionary<ulong, int>();
    }
    
    public async Task<bool> CheckMessageAsync(ulong connectionId, byte[] message)
    {
        try
        {
            // Basic rate limiting check first
            if (!CheckBasicRateLimit(connectionId))
            {
                return false;
            }
            
            // Prepare request for external anti-spam service
            var request = new AntiSpamRequest
            {
                ConnectionId = connectionId,
                MessageHash = ComputeMessageHash(message),
                MessageContent = System.Text.Encoding.UTF8.GetString(message),
                Timestamp = DateTime.UtcNow,
                UserAgent = "AegisServer/1.0"
            };
            
            // Call external anti-spam service via HTTP (gRPC/Thrift alternative)
            var response = await CallAntiSpamServiceAsync(request);
            
            // Update connection statistics
            UpdateConnectionStats(connectionId);
            
            return response.Allowed;
        }
        catch (Exception ex)
        {
            // Log error but allow message to pass through (fail-safe)
            Console.WriteLine($"Anti-spam service error for connection {connectionId}: {ex.Message}");
            return true;
        }
    }
    
    private bool CheckBasicRateLimit(ulong connectionId)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            
            // Clean up old connections (older than 5 minutes)
            var cutoff = now.AddMinutes(-5);
            var oldConnections = _connectionLastMessage
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var oldConn in oldConnections)
            {
                _connectionLastMessage.Remove(oldConn);
                _connectionMessageCount.Remove(oldConn);
            }
            
            // Check rate limit: max 10 messages per minute per connection
            if (_connectionLastMessage.TryGetValue(connectionId, out var lastTime))
            {
                var timeSinceLastMessage = now - lastTime;
                var currentCount = _connectionMessageCount.GetValueOrDefault(connectionId, 0);
                
                // Reset counter if more than a minute has passed
                if (timeSinceLastMessage.TotalMinutes > 1)
                {
                    _connectionMessageCount[connectionId] = 1;
                    _connectionLastMessage[connectionId] = now;
                    return true;
                }
                
                // Check if exceeding rate limit
                if (currentCount >= 10)
                {
                    return false;
                }
            }
            
            return true;
        }
    }
    
    private void UpdateConnectionStats(ulong connectionId)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _connectionLastMessage[connectionId] = now;
            _connectionMessageCount[connectionId] = _connectionMessageCount.GetValueOrDefault(connectionId, 0) + 1;
        }
    }
    
    private string ComputeMessageHash(byte[] message)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(message);
        return Convert.ToBase64String(hash);
    }
    
    private async Task<AntiSpamResponse> CallAntiSpamServiceAsync(AntiSpamRequest request)
    {
        try
        {
            // Serialize request to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            // Call external service
            var response = await _httpClient.PostAsync($"{_serviceUrl}/check", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var antiSpamResponse = System.Text.Json.JsonSerializer.Deserialize<AntiSpamResponse>(responseJson);
                return antiSpamResponse ?? new AntiSpamResponse { Allowed = true };
            }
            else
            {
                // Service unavailable, allow message (fail-safe)
                return new AntiSpamResponse { Allowed = true };
            }
        }
        catch (HttpRequestException)
        {
            // Service unreachable, allow message (fail-safe)
            return new AntiSpamResponse { Allowed = true };
        }
        catch (TaskCanceledException)
        {
            // Timeout, allow message (fail-safe)
            return new AntiSpamResponse { Allowed = true };
        }
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
