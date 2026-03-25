using System.Text;
using Minio;
using Minio.DataModel.Args;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Aegis.Data.Services;

public sealed class AvatarStorageOptions
{
    public const string SectionName = "AvatarStorage";

    public string Provider { get; set; } = "Local";
    public string BaseDirectory { get; set; } = "media/avatars";
    public string PublicBaseUrl { get; set; } = "/media/avatars";
    public int MaxUploadBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxWidth { get; set; } = 512;
    public int MaxHeight { get; set; } = 512;
    public bool AllowDataUrlInput { get; set; } = true;
}

public sealed class MinioStorageOptions
{
    public const string SectionName = "Minio";

    public bool Enabled { get; set; } = false;
    public string Endpoint { get; set; } = "localhost:9000";
    public bool UseSsl { get; set; }
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public string Bucket { get; set; } = "aegis-media";
    public string PublicBaseUrl { get; set; } = "http://localhost:9000";
}

public interface IAvatarStorageService
{
    Task<string> NormalizeAvatarReferenceAsync(string avatarInput, ulong userId, CancellationToken cancellationToken = default);
    Task DeleteIfManagedAsync(string? avatarUrl, CancellationToken cancellationToken = default);
}

public sealed class LocalAvatarStorageService : IAvatarStorageService
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private readonly AvatarStorageOptions _options;
    private readonly ILogger<LocalAvatarStorageService> _logger;

    public LocalAvatarStorageService(
        IOptions<AvatarStorageOptions> options,
        ILogger<LocalAvatarStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;

        Directory.CreateDirectory(_options.BaseDirectory);
    }

    public async Task<string> NormalizeAvatarReferenceAsync(string avatarInput, ulong userId, CancellationToken cancellationToken = default)
    {
        var value = (avatarInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Avatar value is empty", nameof(avatarInput));
        }

        if (!IsDataUrl(value))
        {
            return value;
        }

        if (!_options.AllowDataUrlInput)
        {
            throw new InvalidOperationException("Data URL avatars are disabled");
        }

        var parsed = ParseDataUrl(value);
        if (!AllowedMimeTypes.Contains(parsed.MimeType))
        {
            throw new ArgumentException("Unsupported avatar format. Allowed: jpeg, png, webp");
        }

        if (parsed.Data.Length == 0 || parsed.Data.Length > _options.MaxUploadBytes)
        {
            throw new ArgumentException($"Avatar exceeds size limit {_options.MaxUploadBytes} bytes");
        }

        await using var input = new MemoryStream(parsed.Data, writable: false);
        using var image = await Image.LoadAsync(input, cancellationToken);

        image.Mutate(ctx =>
        {
            ctx.AutoOrient();
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(_options.MaxWidth, _options.MaxHeight),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            });
        });

        var userFolder = Path.Combine(_options.BaseDirectory, userId.ToString());
        Directory.CreateDirectory(userFolder);

        var fileName = $"{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(userFolder, fileName);

        var encoder = new JpegEncoder { Quality = 85 };
        await image.SaveAsync(fullPath, encoder, cancellationToken);

        var relativePath = Path.Combine(userId.ToString(), fileName)
            .Replace(Path.DirectorySeparatorChar, '/');

        return CombinePublicPath(_options.PublicBaseUrl, relativePath);
    }

    public Task DeleteIfManagedAsync(string? avatarUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return Task.CompletedTask;
        }

        try
        {
            var relative = TryMapToRelativeManagedPath(avatarUrl!);
            if (relative == null)
            {
                return Task.CompletedTask;
            }

            var fullPath = Path.Combine(_options.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete managed avatar file for URL {AvatarUrl}", avatarUrl);
        }

        return Task.CompletedTask;
    }

    private static bool IsDataUrl(string value)
    {
        return value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static (string MimeType, byte[] Data) ParseDataUrl(string value)
    {
        var commaIndex = value.IndexOf(',');
        if (commaIndex <= 0)
        {
            throw new ArgumentException("Invalid data URL");
        }

        var header = value[..commaIndex];
        var payload = value[(commaIndex + 1)..];

        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only base64 data URLs are supported");
        }

        var mimeType = header["data:".Length..^";base64".Length].Trim();
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException("Data URL mime type is missing");
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid base64 data in avatar payload");
        }

        return (mimeType, data);
    }

    private static string CombinePublicPath(string basePath, string relativePath)
    {
        var prefix = string.IsNullOrWhiteSpace(basePath) ? "/media/avatars" : basePath.TrimEnd('/');
        return $"{prefix}/{relativePath.TrimStart('/')}";
    }

    private string? TryMapToRelativeManagedPath(string avatarUrl)
    {
        var normalized = avatarUrl.Trim();
        var publicPrefix = _options.PublicBaseUrl.TrimEnd('/');

        if (normalized.StartsWith(publicPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized[(publicPrefix.Length + 1)..];
        }

        return null;
    }
}

public sealed class PassThroughAvatarStorageService : IAvatarStorageService
{
    public Task<string> NormalizeAvatarReferenceAsync(string avatarInput, ulong userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(avatarInput);
    }

    public Task DeleteIfManagedAsync(string? avatarUrl, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class MinioAvatarStorageService : IAvatarStorageService
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private readonly AvatarStorageOptions _avatarOptions;
    private readonly MinioStorageOptions _minioOptions;
    private readonly IMinioClient _minioClient;
    private readonly ILogger<MinioAvatarStorageService> _logger;
    private volatile bool _bucketEnsured;

    public MinioAvatarStorageService(
        IOptions<AvatarStorageOptions> avatarOptions,
        IOptions<MinioStorageOptions> minioOptions,
        ILogger<MinioAvatarStorageService> logger)
    {
        _avatarOptions = avatarOptions.Value;
        _minioOptions = minioOptions.Value;
        _logger = logger;

        _minioClient = new MinioClient()
            .WithEndpoint(_minioOptions.Endpoint)
            .WithCredentials(_minioOptions.AccessKey, _minioOptions.SecretKey)
            .WithSSL(_minioOptions.UseSsl)
            .Build();
    }

    public async Task<string> NormalizeAvatarReferenceAsync(string avatarInput, ulong userId, CancellationToken cancellationToken = default)
    {
        var value = (avatarInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Avatar value is empty", nameof(avatarInput));
        }

        if (!IsDataUrl(value))
        {
            return value;
        }

        if (!_avatarOptions.AllowDataUrlInput)
        {
            throw new InvalidOperationException("Data URL avatars are disabled");
        }

        var parsed = ParseDataUrl(value);
        if (!AllowedMimeTypes.Contains(parsed.MimeType))
        {
            throw new ArgumentException("Unsupported avatar format. Allowed: jpeg, png, webp");
        }

        if (parsed.Data.Length == 0 || parsed.Data.Length > _avatarOptions.MaxUploadBytes)
        {
            throw new ArgumentException($"Avatar exceeds size limit {_avatarOptions.MaxUploadBytes} bytes");
        }

        await EnsureBucketAsync(cancellationToken);

        await using var input = new MemoryStream(parsed.Data, writable: false);
        using var image = await Image.LoadAsync(input, cancellationToken);

        image.Mutate(ctx =>
        {
            ctx.AutoOrient();
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(_avatarOptions.MaxWidth, _avatarOptions.MaxHeight),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            });
        });

        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 85 }, cancellationToken);
        output.Position = 0;

        var objectName = $"avatars/{userId}/{Guid.NewGuid():N}.jpg";

        var putArgs = new PutObjectArgs()
            .WithBucket(_minioOptions.Bucket)
            .WithObject(objectName)
            .WithStreamData(output)
            .WithObjectSize(output.Length)
            .WithContentType("image/jpeg");

        await _minioClient.PutObjectAsync(putArgs, cancellationToken);
        return BuildPublicUrl(objectName);
    }

    public async Task DeleteIfManagedAsync(string? avatarUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return;
        }

        try
        {
            var objectName = TryExtractObjectName(avatarUrl);
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return;
            }

            var removeArgs = new RemoveObjectArgs()
                .WithBucket(_minioOptions.Bucket)
                .WithObject(objectName);

            await _minioClient.RemoveObjectAsync(removeArgs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete MinIO avatar object for URL {AvatarUrl}", avatarUrl);
        }
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketEnsured)
        {
            return;
        }

        var bucketExists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_minioOptions.Bucket),
            cancellationToken);

        if (!bucketExists)
        {
            await _minioClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_minioOptions.Bucket),
                cancellationToken);
        }

        _bucketEnsured = true;
    }

    private string BuildPublicUrl(string objectName)
    {
        var baseUrl = _minioOptions.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{_minioOptions.Bucket}/{objectName}";
    }

    private string? TryExtractObjectName(string avatarUrl)
    {
        var normalized = avatarUrl.Trim();
        var bucketPrefix = $"/{_minioOptions.Bucket}/";
        var bucketIndex = normalized.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);
        if (bucketIndex < 0)
        {
            return null;
        }

        return normalized[(bucketIndex + bucketPrefix.Length)..];
    }

    private static bool IsDataUrl(string value)
    {
        return value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private static (string MimeType, byte[] Data) ParseDataUrl(string value)
    {
        var commaIndex = value.IndexOf(',');
        if (commaIndex <= 0)
        {
            throw new ArgumentException("Invalid data URL");
        }

        var header = value[..commaIndex];
        var payload = value[(commaIndex + 1)..];

        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only base64 data URLs are supported");
        }

        var mimeType = header["data:".Length..^";base64".Length].Trim();
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException("Data URL mime type is missing");
        }

        try
        {
            return (mimeType, Convert.FromBase64String(payload));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid base64 data in avatar payload");
        }
    }
}
