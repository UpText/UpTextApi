using Microsoft.Extensions.Options;

namespace UpTextApi.Services;

public sealed class SqlLogCleanupService(
    ISqlLog sqlLog,
    IOptions<SqlLogOptions> options,
    ILogger<SqlLogCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);
    private readonly SqlLogOptions sqlLogOptions = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sqlLogOptions.CleanupEnabled)
        {
            logger.LogInformation("SQL log retention cleanup is disabled");
            return;
        }

        if (sqlLogOptions.RetentionDays <= 0 || sqlLogOptions.CleanupBatchSize <= 0)
        {
            logger.LogWarning(
                "SQL log retention cleanup is disabled because RetentionDays and CleanupBatchSize must be greater than zero");
            return;
        }

        await CleanupAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deletedRows = await sqlLog.PurgeOlderThanAsync(
                TimeSpan.FromDays(sqlLogOptions.RetentionDays),
                sqlLogOptions.CleanupBatchSize,
                cancellationToken);

            logger.LogInformation(
                "SQL log retention cleanup deleted {DeletedRows} rows older than {RetentionDays} days",
                deletedRows,
                sqlLogOptions.RetentionDays);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "SQL log retention cleanup skipped because SQL logging is not configured");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SQL log retention cleanup failed");
        }
    }
}
