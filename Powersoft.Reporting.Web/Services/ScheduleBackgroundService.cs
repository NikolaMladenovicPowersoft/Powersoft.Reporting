namespace Powersoft.Reporting.Web.Services;

/// <summary>
/// Hosted service that periodically invokes <see cref="ScheduleExecutionService.RunAllDueSchedulesAsync"/>
/// so that scheduled reports execute without any external cron / Task Scheduler trigger.
/// Interval is configurable via appsettings key <c>Scheduler:IntervalSeconds</c> (default 60).
/// </summary>
public sealed class ScheduleBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduleBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public ScheduleBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ScheduleBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var seconds = configuration.GetValue("Scheduler:IntervalSeconds", 60);
        _interval = TimeSpan.FromSeconds(Math.Max(seconds, 10));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Schedule background service started — polling every {Interval}s", _interval.TotalSeconds);

        // Small initial delay to let the rest of the app start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ScheduleExecutionService>();
                var summary = await runner.RunAllDueSchedulesAsync(stoppingToken);

                if (summary.Processed > 0)
                {
                    _logger.LogInformation(
                        "Background run: {Processed} processed, {Succeeded} succeeded, {Failed} failed",
                        summary.Processed, summary.Succeeded, summary.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in schedule background service tick");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Schedule background service stopped");
    }
}
