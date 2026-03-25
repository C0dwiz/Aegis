using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aegis.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Data.Services;

public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "http://localhost:9200";
    public string IndexName { get; set; } = "aegis-users";
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public interface IUserSearchIndexService
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<ulong>> SearchUserIdsByUsernameAsync(string pattern, int limit, CancellationToken cancellationToken = default);
    Task IndexUserAsync(User user, CancellationToken cancellationToken = default);
}

public sealed class NoOpUserSearchIndexService : IUserSearchIndexService
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<ulong>> SearchUserIdsByUsernameAsync(string pattern, int limit, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ulong>>(Array.Empty<ulong>());
    }

    public Task IndexUserAsync(User user, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class ElasticsearchUserSearchIndexService : IUserSearchIndexService
{
    private readonly HttpClient _httpClient;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchUserSearchIndexService> _logger;

    public ElasticsearchUserSearchIndexService(
        HttpClient httpClient,
        IOptions<ElasticsearchOptions> options,
        ILogger<ElasticsearchUserSearchIndexService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/");

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", _options.ApiKey);
        }
        else if (!string.IsNullOrWhiteSpace(_options.Username) && !string.IsNullOrWhiteSpace(_options.Password))
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    public bool IsEnabled => _options.Enabled;

    public async Task<IReadOnlyList<ulong>> SearchUserIdsByUsernameAsync(string pattern, int limit, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Array.Empty<ulong>();
        }

        var normalized = (pattern ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<ulong>();
        }

        var safeLimit = Math.Clamp(limit, 1, 100);
        var body = new
        {
            size = safeLimit,
            _source = new[] { "id" },
            query = new
            {
                @bool = new
                {
                    should = new object[]
                    {
                        new { prefix = new { username = normalized.ToLowerInvariant() } },
                        new { match_phrase_prefix = new { username = normalized } }
                    },
                    minimum_should_match = 1
                }
            }
        };

        try
        {
            using var response = await _httpClient.PostAsync(
                $"{_options.IndexName}/_search",
                JsonContent.Create(body),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<ulong>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!json.RootElement.TryGetProperty("hits", out var hitsNode) ||
                !hitsNode.TryGetProperty("hits", out var hitItems) ||
                hitItems.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ulong>();
            }

            var ids = new List<ulong>(safeLimit);
            foreach (var hit in hitItems.EnumerateArray())
            {
                if (!hit.TryGetProperty("_source", out var source))
                {
                    continue;
                }

                if (!source.TryGetProperty("id", out var idNode))
                {
                    continue;
                }

                if (idNode.ValueKind == JsonValueKind.Number && idNode.TryGetUInt64(out var numericId))
                {
                    ids.Add(numericId);
                    continue;
                }

                if (idNode.ValueKind == JsonValueKind.String && ulong.TryParse(idNode.GetString(), out var parsedId))
                {
                    ids.Add(parsedId);
                }
            }

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elasticsearch username search failed for pattern {Pattern}", normalized);
            return Array.Empty<ulong>();
        }
    }

    public async Task IndexUserAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || user.Id == 0)
        {
            return;
        }

        var body = new
        {
            id = user.Id,
            username = user.Username,
            displayName = user.DisplayName,
            email = user.Email,
            isActive = user.IsActive,
            updatedAt = user.UpdatedAt
        };

        try
        {
            using var response = await _httpClient.PutAsync(
                $"{_options.IndexName}/_doc/{user.Id}",
                JsonContent.Create(body),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Elasticsearch indexing returned status {StatusCode} for user {UserId}", response.StatusCode, user.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elasticsearch indexing failed for user {UserId}", user.Id);
        }
    }
}
