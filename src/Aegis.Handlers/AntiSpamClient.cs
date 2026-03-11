using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using MathNet.Numerics.LinearAlgebra;

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

public class AntiSpamClient : IAntiSpamClient, IDisposable
{
    private sealed class FeatureBundle
    {
        public required Vector<double> Features { get; init; }
        public required string[] Tokens { get; init; }
        public required int UrlCount { get; init; }
        public required double LexiconHitRatio { get; init; }
        public required double RepeatedRatio { get; init; }
        public required double UpperRatio { get; init; }
    }

    private sealed class ModelSnapshot
    {
        public double[] Weights { get; set; } = Array.Empty<double>();
    }

    private sealed class ConnectionRateState
    {
        public long WindowStartTicks;
        public int MessageCount;
        public long LastSeenTicks;
    }

    private const int FeatureCount = 9;

    private readonly ConcurrentDictionary<ulong, ConnectionRateState> _connectionState;
    private readonly ConcurrentDictionary<string, int> _candidateTokenHits;
    private readonly HashSet<string> _lexicon;
    private readonly object _modelSync = new();
    private readonly object _lexiconSync = new();

    private Vector<double> _weights;

    private readonly TimeSpan _localRateWindow = TimeSpan.FromMinutes(1);
    private readonly int _maxMessagesPerWindow = 10;
    private readonly TimeSpan _entryTtl = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _flushPeriod = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _snapshotPeriod = TimeSpan.FromMinutes(3);

    private readonly double _learningRate = 0.06;
    private readonly double _spamThreshold = 0.83;
    private readonly int _autoPromoteTokenHits = 4;

    private readonly string _lexiconFilePath;
    private readonly string _modelFilePath;

    private readonly Timer _cleanupTimer;
    private readonly Timer _flushLexiconTimer;
    private readonly Timer _saveModelTimer;
    
    public AntiSpamClient(string serviceUrl = "http://localhost:8080")
    {
        _connectionState = new ConcurrentDictionary<ulong, ConnectionRateState>();
        _candidateTokenHits = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _lexicon = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _weights = Vector<double>.Build.DenseOfArray(new[]
        {
            -2.1, // bias
            0.65, // length normalized
            1.9,  // repeated characters ratio
            1.4,  // uppercase ratio
            1.1,  // digit ratio
            2.2,  // url flag/count
            1.6,  // punctuation ratio
            2.5,  // lexicon hit ratio
            1.2   // token burst
        });

        var dataDir = ResolveDataDirectory();
        _lexiconFilePath = Path.Combine(dataDir, "spam-words.txt");
        _modelFilePath = Path.Combine(dataDir, "spam-model.json");

        EnsureLexiconFileExists();
        LoadLexiconFromFile();
        LoadModelIfExists();

        _cleanupTimer = new Timer(CleanupStaleConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _flushLexiconTimer = new Timer(_ => FlushCandidateTokens(), null, _flushPeriod, _flushPeriod);
        _saveModelTimer = new Timer(_ => SaveModelSnapshot(), null, _snapshotPeriod, _snapshotPeriod);
    }
    
    public async Task<bool> CheckMessageAsync(ulong connectionId, byte[] message)
    {
        try
        {
            if (!CheckBasicRateLimit(connectionId))
            {
                return false;
            }

            var sample = ExtractFeatures(message);
            var analysis = Analyze(sample);
            UpdateOnlineModel(sample);
            UpdateDynamicLexicon(sample);
            UpdateConnectionStats(connectionId);

            var allowed = analysis.Score < _spamThreshold;
            return await Task.FromResult(allowed);
        }
        catch (Exception)
        {
            return await Task.FromResult(true);
        }
    }

    private AntiSpamResponse Analyze(FeatureBundle bundle)
    {
        double probability;

        lock (_modelSync)
        {
            probability = Sigmoid(_weights.DotProduct(bundle.Features));
        }

        return new AntiSpamResponse
        {
            Allowed = probability < _spamThreshold,
            Score = probability,
            Reason = probability < _spamThreshold ? string.Empty : "local_mathnet_model"
        };
    }

    private FeatureBundle ExtractFeatures(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return new FeatureBundle
            {
                Features = Vector<double>.Build.Dense(FeatureCount),
                Tokens = Array.Empty<string>(),
                UrlCount = 0,
                LexiconHitRatio = 0,
                RepeatedRatio = 0,
                UpperRatio = 0
            };
        }

        var len = payload.Length;
        var upper = 0;
        var digits = 0;
        var punct = 0;
        var repeated = 0;

        for (var i = 0; i < payload.Length; i++)
        {
            var b = payload[i];
            if (IsAsciiUpper(b)) upper++;
            if (IsAsciiDigit(b)) digits++;
            if (IsAsciiPunctuation(b)) punct++;
            if (i > 0 && payload[i] == payload[i - 1]) repeated++;
        }

        var urlCount = CountUrls(payload);
        var tokens = Tokenize(payload);
        var lexiconHits = CountLexiconHits(tokens);
        var burst = Math.Min(1.0, (double)tokens.Length / 40.0);

        var features = Vector<double>.Build.DenseOfArray(new[]
        {
            1.0,
            Math.Min(1.0, (double)len / 512.0),
            (double)repeated / len,
            (double)upper / len,
            (double)digits / len,
            Math.Min(1.0, (double)urlCount / 3.0),
            (double)punct / len,
            tokens.Length == 0 ? 0.0 : (double)lexiconHits / tokens.Length,
            burst
        });

        return new FeatureBundle
        {
            Features = features,
            Tokens = tokens,
            UrlCount = urlCount,
            LexiconHitRatio = tokens.Length == 0 ? 0.0 : (double)lexiconHits / tokens.Length,
            RepeatedRatio = (double)repeated / len,
            UpperRatio = (double)upper / len
        };
    }

    private void UpdateOnlineModel(FeatureBundle sample)
    {
        // Pseudo-label from deterministic rules; can be replaced with moderator labels.
        var label = InferPseudoLabel(sample);

        lock (_modelSync)
        {
            var probability = Sigmoid(_weights.DotProduct(sample.Features));
            var gradient = sample.Features.Multiply(probability - label);
            _weights = _weights.Subtract(gradient.Multiply(_learningRate));
        }
    }

    private double InferPseudoLabel(FeatureBundle sample)
    {
        if (sample.UrlCount >= 2)
        {
            return 1.0;
        }

        if (sample.LexiconHitRatio >= 0.5)
        {
            return 1.0;
        }

        if (sample.RepeatedRatio > 0.25 && sample.UpperRatio > 0.35)
        {
            return 1.0;
        }

        return 0.0;
    }

    private void UpdateDynamicLexicon(FeatureBundle sample)
    {
        foreach (var token in sample.Tokens)
        {
            if (!CanPromoteToken(token))
            {
                continue;
            }

            if (_lexicon.Contains(token))
            {
                continue;
            }

            var hits = _candidateTokenHits.AddOrUpdate(token, 1, (_, current) => current + 1);
            if (hits < _autoPromoteTokenHits)
            {
                continue;
            }

            lock (_lexiconSync)
            {
                if (_lexicon.Add(token))
                {
                    AppendLexiconWord(token);
                }
            }

            _candidateTokenHits.TryRemove(token, out _);
        }
    }

    private static bool CanPromoteToken(string token)
    {
        if (token.Length < 5 || token.Length > 32)
        {
            return false;
        }

        if (token.All(char.IsDigit))
        {
            return false;
        }

        return token.Any(char.IsLetter);
    }
    
    private bool CheckBasicRateLimit(ulong connectionId)
    {
        var now = DateTime.UtcNow;
        var state = _connectionState.GetOrAdd(connectionId, _ => new ConnectionRateState
        {
            WindowStartTicks = now.Ticks,
            LastSeenTicks = now.Ticks,
            MessageCount = 0
        });

        lock (state)
        {
            var windowStart = new DateTime(state.WindowStartTicks, DateTimeKind.Utc);
            if (now - windowStart >= _localRateWindow)
            {
                state.WindowStartTicks = now.Ticks;
                state.MessageCount = 0;
            }

            if (state.MessageCount >= _maxMessagesPerWindow)
            {
                state.LastSeenTicks = now.Ticks;
                return false;
            }

            state.MessageCount++;
            state.LastSeenTicks = now.Ticks;
            return true;
        }
    }
    
    private void UpdateConnectionStats(ulong connectionId)
    {
        if (_connectionState.TryGetValue(connectionId, out var state))
        {
            state.LastSeenTicks = DateTime.UtcNow.Ticks;
        }
    }
    
    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _flushLexiconTimer.Dispose();
        _saveModelTimer.Dispose();
        FlushCandidateTokens();
        SaveModelSnapshot();
    }

    private void CleanupStaleConnections(object? state)
    {
        var cutoffTicks = DateTime.UtcNow.Subtract(_entryTtl).Ticks;
        foreach (var entry in _connectionState)
        {
            if (entry.Value.LastSeenTicks < cutoffTicks)
            {
                _connectionState.TryRemove(entry.Key, out _);
            }
        }
    }

    private void FlushCandidateTokens()
    {
        foreach (var entry in _candidateTokenHits.ToArray())
        {
            if (entry.Value < _autoPromoteTokenHits)
            {
                continue;
            }

            lock (_lexiconSync)
            {
                if (_lexicon.Add(entry.Key))
                {
                    AppendLexiconWord(entry.Key);
                }
            }

            _candidateTokenHits.TryRemove(entry.Key, out _);
        }
    }

    private void AppendLexiconWord(string token)
    {
        File.AppendAllText(_lexiconFilePath, token + Environment.NewLine, Encoding.UTF8);
    }

    private static int CountUrls(ReadOnlySpan<byte> payload)
    {
        var count = 0;
        if (ContainsAsciiIgnoreCase(payload, "http://"u8)) count++;
        if (ContainsAsciiIgnoreCase(payload, "https://"u8)) count++;
        if (ContainsAsciiIgnoreCase(payload, "t.me/"u8)) count++;
        if (ContainsAsciiIgnoreCase(payload, "bit.ly/"u8)) count++;
        return count;
    }

    private static string[] Tokenize(ReadOnlySpan<byte> payload)
    {
        var result = new List<string>();
        Span<char> scratch = stackalloc char[64];
        var tokenLength = 0;

        foreach (var b in payload)
        {
            if (IsAsciiLetterOrDigit(b))
            {
                if (tokenLength < scratch.Length)
                {
                    scratch[tokenLength] = (char)AsciiToLower(b);
                }

                tokenLength++;
                continue;
            }

            if (tokenLength > 0)
            {
                AddToken(result, scratch, tokenLength);
                tokenLength = 0;
            }
        }

        if (tokenLength > 0)
        {
            AddToken(result, scratch, tokenLength);
        }

        return result.ToArray();
    }

    private static void AddToken(List<string> result, Span<char> scratch, int tokenLength)
    {
        if (tokenLength <= 0)
        {
            return;
        }

        if (tokenLength <= scratch.Length)
        {
            result.Add(new string(scratch.Slice(0, tokenLength)));
            return;
        }

        // Rare long token path: skip to avoid extra allocations in hot path.
    }

    private static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> pattern)
    {
        if (pattern.Length == 0 || payload.Length < pattern.Length)
        {
            return false;
        }

        for (var i = 0; i <= payload.Length - pattern.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (AsciiToLower(payload[i + j]) != AsciiToLower(pattern[j]))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiUpper(byte b) => b is >= (byte)'A' and <= (byte)'Z';

    private static bool IsAsciiDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool IsAsciiLetterOrDigit(byte b)
    {
        var lower = AsciiToLower(b);
        return (lower is >= (byte)'a' and <= (byte)'z') || IsAsciiDigit(b);
    }

    private static bool IsAsciiPunctuation(byte b)
    {
        return b is >= 33 and <= 47 or >= 58 and <= 64 or >= 91 and <= 96 or >= 123 and <= 126;
    }

    private static byte AsciiToLower(byte b)
    {
        if (b is >= (byte)'A' and <= (byte)'Z')
        {
            return (byte)(b + 32);
        }

        return b;
    }

    private int CountLexiconHits(string[] tokens)
    {
        var hits = 0;
        lock (_lexiconSync)
        {
            foreach (var token in tokens)
            {
                if (_lexicon.Contains(token))
                {
                    hits++;
                }
            }
        }

        return hits;
    }

    private void EnsureLexiconFileExists()
    {
        if (File.Exists(_lexiconFilePath))
        {
            return;
        }

        var defaults = new[]
        {
            "free",
            "bonus",
            "crypto",
            "airdrop",
            "casino",
            "click"
        };

        File.WriteAllLines(_lexiconFilePath, defaults, Encoding.UTF8);
    }

    private void LoadLexiconFromFile()
    {
        lock (_lexiconSync)
        {
            foreach (var line in File.ReadLines(_lexiconFilePath, Encoding.UTF8))
            {
                var token = line.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _lexicon.Add(token);
                }
            }
        }
    }

    private void LoadModelIfExists()
    {
        if (!File.Exists(_modelFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_modelFilePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<ModelSnapshot>(json);
            if (snapshot?.Weights is { Length: FeatureCount })
            {
                _weights = Vector<double>.Build.DenseOfArray(snapshot.Weights);
            }
        }
        catch
        {
            // Keep default weights when snapshot is malformed.
        }
    }

    private void SaveModelSnapshot()
    {
        try
        {
            ModelSnapshot snapshot;
            lock (_modelSync)
            {
                snapshot = new ModelSnapshot { Weights = _weights.ToArray() };
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_modelFilePath, json, Encoding.UTF8);
        }
        catch
        {
            // Best effort persistence.
        }
    }

    private static double Sigmoid(double x)
    {
        if (x >= 0)
        {
            var z = Math.Exp(-x);
            return 1.0 / (1.0 + z);
        }

        var k = Math.Exp(x);
        return k / (1.0 + k);
    }

    private static string ResolveDataDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "antispam-data");

        try
        {
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "antispam-data");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
