namespace UpApi.Services;

public interface ISqlLog
{
    Task EnsureTableAsync(CancellationToken cancellationToken = default);
    Task LogAsync(SqlLogEntry entry, CancellationToken cancellationToken = default);
    Task<long> PurgeOlderThanAsync(
        TimeSpan retention,
        int batchSize,
        CancellationToken cancellationToken = default);
    Task<ServiceLogStatusResult> ReadServiceStatusAsync(
        string service,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SqlLogRow>> ReadAsync(
        string? service,
        int? maxHours,
        int? aboveTime,
        bool? onlySqlError,
        bool? userError,
        bool? today,
        string? procedure,
        int fromRow,
        int toRow,
        SqlLogSortColumn sortColumn,
        SqlLogSortDirection sortDirection,
        CancellationToken cancellationToken = default);
}
