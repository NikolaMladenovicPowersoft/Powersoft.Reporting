using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Powersoft.Reporting.Web.Options;

namespace Powersoft.Reporting.Web.Services.Storage;

/// <summary>
/// Uploads generated reports to DigitalOcean Spaces (S3-compatible).
/// Falls back gracefully (logs warning) when credentials are not configured.
/// </summary>
public sealed class S3ReportStorageService : IReportStorageService, IDisposable
{
    private readonly StorageOptions _opt;
    private readonly ILogger<S3ReportStorageService> _logger;
    private readonly AmazonS3Client? _client;

    public bool IsConfigured => _client != null;

    public S3ReportStorageService(IOptions<StorageOptions> opt, ILogger<S3ReportStorageService> logger)
    {
        _opt = opt.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_opt.AccessKey) || string.IsNullOrWhiteSpace(_opt.ServiceUrl))
        {
            _logger.LogWarning("S3 storage not configured — report uploads will be skipped");
            return;
        }

        var config = new AmazonS3Config
        {
            ServiceURL = _opt.ServiceUrl,
            ForcePathStyle = true
        };

        var credentials = new BasicAWSCredentials(_opt.AccessKey, _opt.SecretKey);
        _client = new AmazonS3Client(credentials, config);
    }

    public async Task<string> UploadAsync(string fileName, byte[] content, string contentType, CancellationToken ct = default)
    {
        if (_client == null)
            throw new InvalidOperationException("S3 storage is not configured");

        var key = $"{_opt.KeyPrefix}{DateTime.UtcNow:yyyy/MM/dd}/{fileName}";

        var request = new PutObjectRequest
        {
            BucketName = _opt.BucketName,
            Key = key,
            ContentType = contentType,
            InputStream = new MemoryStream(content),
            CannedACL = S3CannedACL.Private
        };

        await _client.PutObjectAsync(request, ct);
        _logger.LogInformation("Uploaded report to S3: {Key} ({Size} bytes)", key, content.Length);
        return key;
    }

    public string GetDownloadUrl(string key, TimeSpan? expiry = null)
    {
        if (_client == null)
            throw new InvalidOperationException("S3 storage is not configured");

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _opt.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(24))
        };

        return _client.GetPreSignedURL(request);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
