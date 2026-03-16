using Powersoft.Reporting.Core.Constants;
using Powersoft.Reporting.Core.Models;
using Powersoft.Reporting.Web.Services.Storage;

namespace Powersoft.Reporting.Web.Services;

/// <summary>
/// Periodically deletes generated report files older than the configured retention period.
/// Runs once every 24 hours.
/// </summary>
public sealed class RetentionCleanupService : BackgroundService
{
    private readonly IReportStorageService _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RetentionCleanupService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public RetentionCleanupService(
        IReportStorageService storage,
        IConfiguration configuration,
        ILogger<RetentionCleanupService> logger)
    {
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Retention cleanup service started — runs every {Interval}", Interval);

        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_storage.IsConfigured)
                {
                    int retentionDays = _configuration.GetValue("Reporting:RetentionDays",
                        ModuleConstants.DefaultRetentionDays);
                    var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

                    _logger.LogInformation("Running retention cleanup: deleting files older than {Days} days (cutoff: {Cutoff:u})",
                        retentionDays, cutoff);

                    int deleted = await _storage.DeleteOlderThanAsync(cutoff, stoppingToken);
                    _logger.LogInformation("Retention cleanup complete: {Deleted} file(s) removed", deleted);
                }
                else
                {
                    _logger.LogDebug("Storage not configured — skipping retention cleanup");
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
