namespace Powersoft.Reporting.Web.Services.Storage;

public interface IReportStorageService
{
    /// <summary>
    /// Uploads a generated report file and returns the storage key (path).
    /// </summary>
    Task<string> UploadAsync(string fileName, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Generates a pre-signed download URL valid for the specified duration.
    /// </summary>
    string GetDownloadUrl(string key, TimeSpan? expiry = null);

    /// <summary>Deletes objects older than the specified date. Returns count of deleted objects.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);

    /// <summary>Returns true when storage is properly configured.</summary>
    bool IsConfigured { get; }
}
