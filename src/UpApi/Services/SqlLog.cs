using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace UpApi.Services;

public sealed class SqlLog(IOptions<SqlLogOptions> options, ILogger<SqlLog> logger) : ISqlLog
{
    private readonly SqlLogOptions sqlLogOptions = options.Value;

    public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            return;
        }

        const string lookupTableNameParameter = "@tableName";
        const string lookupSchemaParameter = "@schema";

        var sqlCreate = $"""
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = {lookupTableNameParameter} AND TABLE_SCHEMA = {lookupSchemaParameter}
)
BEGIN
    CREATE TABLE [{schema}].[{tableName}](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [ApiName] [nvarchar](max) NULL,
        [SwaServer] [nvarchar](max) NULL,
        [MsUsed] [int] NULL,
        [TimeStamp] [datetime] DEFAULT GETDATE(),
        [ReturnValue] [int] NULL,
        [RequestBody] [nvarchar](max) NULL,
        [ReturnBody] [nvarchar](max) NULL,
        [ExecString] [nvarchar](max) NULL,
        [jwt] [nvarchar](max) NULL,
        [UnexpectedError] [nvarchar](max) NULL
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID('[{schema}].[{tableName}]')
      AND type = 'PK'
)
BEGIN
    ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED
    (
        [Id] ASC
    ) WITH (
        PAD_INDEX = OFF,
        STATISTICS_NORECOMPUTE = OFF,
        SORT_IN_TEMPDB = OFF,
        IGNORE_DUP_KEY = OFF,
        ONLINE = OFF,
        ALLOW_ROW_LOCKS = ON,
        ALLOW_PAGE_LOCKS = ON
    ) ON [PRIMARY]
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('[{schema}].[{tableName}]')
      AND name = 'IX_{tableName}_TimeStamp'
)
BEGIN
    CREATE INDEX [IX_{tableName}_TimeStamp]
        ON [{schema}].[{tableName}] ([TimeStamp]);
END;
""";

        try
        {
            await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sqlCreate, connection);
            command.Parameters.Add(new SqlParameter(lookupTableNameParameter, SqlDbType.NVarChar, 128) { Value = tableName });
            command.Parameters.Add(new SqlParameter(lookupSchemaParameter, SqlDbType.NVarChar, 128) { Value = schema });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize SQL log table {Schema}.{TableName}", schema, tableName);
        }
    }

    public async Task LogAsync(SqlLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            return;
        }

        var sqlInsert = $"""
INSERT INTO [{schema}].[{tableName}]
    (ApiName, SwaServer, MsUsed, ReturnValue, RequestBody, ReturnBody, ExecString, jwt, UnexpectedError)
VALUES
    (@ApiName, @SwaServer, @MsUsed, @ReturnValue, @RequestBody, @ReturnBody, @ExecString, @Jwt, @UnexpectedError);
""";

        try
        {
            await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sqlInsert, connection);
            command.Parameters.AddWithValue("@ApiName", entry.ApiName);
            command.Parameters.AddWithValue("@SwaServer", Environment.MachineName);
            command.Parameters.AddWithValue("@MsUsed", ParseMsUsed(entry.MsUsed));
            command.Parameters.AddWithValue("@ReturnValue", entry.ReturnValue);
            command.Parameters.AddWithValue("@RequestBody", ToDbValue(entry.RequestBody));
            command.Parameters.AddWithValue("@ReturnBody", ToDbValue(entry.ReturnBody));
            command.Parameters.AddWithValue("@ExecString", ToDbValue(entry.ExecString));
            command.Parameters.AddWithValue("@Jwt", ToDbValue(entry.Jwt));
            command.Parameters.AddWithValue("@UnexpectedError", ToDbValue(entry.UnexpectedError));
            await command.ExecuteNonQueryAsync(cancellationToken);
            WriteConsoleLine(entry, schema, tableName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write SQL log entry for {ApiName}", entry.ApiName);
        }
    }

    public async Task<long> PurgeOlderThanAsync(
        TimeSpan retention,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be greater than zero.");
        }

        if (retention.TotalDays > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Retention is too large.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            throw new InvalidOperationException("SqlLog is not configured.");
        }

        var sqlDelete = $"""
DECLARE @Deleted int = 1;
DECLARE @TotalDeleted bigint = 0;
DECLARE @Cutoff datetime = DATEADD(DAY, -@RetentionDays, GETDATE());

WHILE @Deleted > 0
BEGIN
    DELETE TOP (@BatchSize)
    FROM [{schema}].[{tableName}]
    WHERE [TimeStamp] < @Cutoff;

    SET @Deleted = @@ROWCOUNT;
    SET @TotalDeleted += @Deleted;
END;

SELECT @TotalDeleted;
""";

        await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sqlDelete, connection);
        command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = batchSize });
        command.Parameters.Add(new SqlParameter("@RetentionDays", SqlDbType.Int)
        {
            Value = Convert.ToInt32(Math.Ceiling(retention.TotalDays))
        });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<SqlLogRow>> ReadAsync(
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
        CancellationToken cancellationToken = default)
    {
        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            throw new InvalidOperationException("SqlLog is not configured.");
        }

        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(service))
        {
            filters.Add("[ApiName] = @Service");
        }

        if (maxHours.HasValue)
        {
            filters.Add("[TimeStamp] >= DATEADD(HOUR, -@MaxHours, GETDATE())");
        }

        if (aboveTime.HasValue)
        {
            filters.Add("[MsUsed] > @AboveTime");
        }

        if (today == true)
        {
            filters.Add("[TimeStamp] >= CONVERT(date, GETDATE())");
        }

        if (!string.IsNullOrWhiteSpace(procedure))
        {
            filters.Add("([ExecString] LIKE @ProcedureExecution ESCAPE '~' OR [ExecString] LIKE @SchemaProcedureExecution ESCAPE '~')");
        }

        if (onlySqlError == true && userError == true)
        {
            filters.Add("[ReturnValue] >= 400");
        }
        else if (onlySqlError == true)
        {
            filters.Add("[ReturnValue] >= 500");
        }
        else if (userError == true)
        {
            filters.Add("[ReturnValue] >= 400 AND [ReturnValue] < 500");
        }

        var whereClause = filters.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", filters)}";
        var orderByColumn = sortColumn switch
        {
            SqlLogSortColumn.Time => "[TimeStamp]",
            SqlLogSortColumn.Service => "[ApiName]",
            SqlLogSortColumn.Status => "[ReturnValue]",
            SqlLogSortColumn.Duration => "[MsUsed]",
            SqlLogSortColumn.Server => "[SwaServer]",
            SqlLogSortColumn.SqlExecution => "[ExecString]",
            _ => throw new ArgumentOutOfRangeException(nameof(sortColumn))
        };
        var orderByDirection = sortDirection == SqlLogSortDirection.Asc ? "ASC" : "DESC";
        var orderByClause = sortColumn == SqlLogSortColumn.Time
            ? $"{orderByColumn} {orderByDirection}, [Id] {orderByDirection}"
            : $"{orderByColumn} {orderByDirection}, [TimeStamp] DESC, [Id] DESC";
        var sqlSelect = $"""
SELECT
    [Id],
    [ApiName],
    [SwaServer],
    [MsUsed],
    [TimeStamp],
    [ReturnValue],
    [RequestBody],
    [ReturnBody],
    [ExecString],
    [jwt],
    [UnexpectedError]
FROM [{schema}].[{tableName}]
{whereClause}
ORDER BY {orderByClause}
OFFSET @FromRow ROWS
FETCH NEXT @RowCount ROWS ONLY;
""";

        await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sqlSelect, connection);

        if (!string.IsNullOrWhiteSpace(service))
        {
            command.Parameters.Add(new SqlParameter("@Service", SqlDbType.NVarChar, -1) { Value = service });
        }

        if (maxHours.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@MaxHours", SqlDbType.Int) { Value = maxHours.Value });
        }

        if (aboveTime.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@AboveTime", SqlDbType.Int) { Value = aboveTime.Value });
        }

        if (!string.IsNullOrWhiteSpace(procedure))
        {
            var procedureSearch = EscapeLikeValue(procedure.Trim());
            command.Parameters.Add(new SqlParameter("@ProcedureExecution", SqlDbType.NVarChar, -1) { Value = $"EXEC {procedureSearch}%" });
            command.Parameters.Add(new SqlParameter("@SchemaProcedureExecution", SqlDbType.NVarChar, -1) { Value = $"EXEC %.%{procedureSearch}%" });
        }

        command.Parameters.Add(new SqlParameter("@FromRow", SqlDbType.Int) { Value = fromRow });
        command.Parameters.Add(new SqlParameter("@RowCount", SqlDbType.Int) { Value = toRow - fromRow + 1 });

        var rows = new List<SqlLogRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SqlLogRow(
                Id: reader.GetInt32(0),
                ApiName: GetNullableString(reader, 1),
                SwaServer: GetNullableString(reader, 2),
                MsUsed: GetNullableInt32(reader, 3),
                TimeStamp: GetNullableDateTime(reader, 4),
                ReturnValue: GetNullableInt32(reader, 5),
                RequestBody: GetNullableString(reader, 6),
                ReturnBody: GetNullableString(reader, 7),
                ExecString: GetNullableString(reader, 8),
                Jwt: GetNullableString(reader, 9),
                UnexpectedError: GetNullableString(reader, 10)));
        }

        return rows;
    }

    public async Task<ServiceLogStatusResult> ReadServiceStatusAsync(
        string service,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetValidatedIdentifiers(out var schema, out var tableName))
        {
            throw new InvalidOperationException("SqlLog is not configured.");
        }

        var sqlSelect = $"""
SELECT
    COALESCE(SUM(CASE
        WHEN [TimeStamp] >= DATEADD(HOUR, -1, GETDATE()) THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [CallsLastHours],
    COALESCE(SUM(CASE
        WHEN [TimeStamp] >= DATEADD(HOUR, -1, GETDATE())
         AND [ReturnValue] >= 400 AND [ReturnValue] < 500 THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [UserErrorsLastHour],
    COALESCE(SUM(CASE
        WHEN [TimeStamp] >= DATEADD(HOUR, -1, GETDATE())
         AND [ReturnValue] >= 500 THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [SqlErrorsLastHour],
    COALESCE(SUM(CASE
        WHEN [TimeStamp] >= CONVERT(date, GETDATE()) THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [CallsToDay],
    COALESCE(SUM(CASE
        WHEN [TimeStamp] >= CONVERT(date, GETDATE())
         AND [ReturnValue] >= 400 AND [ReturnValue] < 500 THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [UserErrorsToDay],
    COALESCE(SUM(CASE
        WHEN [TimeStamp] >= CONVERT(date, GETDATE())
         AND [ReturnValue] >= 500 THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [SqlErrorsToDay],
    COUNT_BIG(*) AS [Calls],
    COALESCE(SUM(CASE
        WHEN [ReturnValue] >= 400 AND [ReturnValue] < 500 THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [UserErrors],
    COALESCE(SUM(CASE
        WHEN [ReturnValue] >= 500 THEN CAST(1 AS bigint)
        ELSE CAST(0 AS bigint)
    END), 0) AS [SqlErrors]
FROM [{schema}].[{tableName}]
WHERE [ApiName] = @Service;
""";

        await using var connection = new SqlConnection(sqlLogOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sqlSelect, connection);
        command.Parameters.Add(new SqlParameter("@Service", SqlDbType.NVarChar, -1) { Value = service });

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ServiceLogStatusResult(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        return new ServiceLogStatusResult(
            CallsLastHours: reader.GetInt64(0),
            UserErrorsLastHour: reader.GetInt64(1),
            SqlErrorsLastHour: reader.GetInt64(2),
            CallsToDay: reader.GetInt64(3),
            UserErrorsToDay: reader.GetInt64(4),
            SqlErrorsToDay: reader.GetInt64(5),
            Calls: reader.GetInt64(6),
            UserErrors: reader.GetInt64(7),
            SqlErrors: reader.GetInt64(8));
    }

    private bool TryGetValidatedIdentifiers(
        [NotNullWhen(true)] out string? schema,
        [NotNullWhen(true)] out string? tableName)
    {
        schema = null;
        tableName = null;

        if (string.IsNullOrWhiteSpace(sqlLogOptions.ConnectionString))
        {
            return false;
        }

        if (!IsSafeSqlIdentifier(sqlLogOptions.Schema) || !IsSafeSqlIdentifier(sqlLogOptions.TableName))
        {
            logger.LogWarning("Skipping SQL logging because configured schema/table are not valid SQL identifiers");
            return false;
        }

        schema = sqlLogOptions.Schema;
        tableName = sqlLogOptions.TableName;
        return true;
    }

    private static bool IsSafeSqlIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static int ParseMsUsed(string value)
        => int.TryParse(value, out var msUsed) ? msUsed : 0;

    private static object ToDbValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string EscapeLikeValue(string value)
        => value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);

    private static string? GetNullableString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? GetNullableInt32(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static DateTime? GetNullableDateTime(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static void WriteConsoleLine(SqlLogEntry entry, string schema, string tableName)
    {
        var originalColor = Console.ForegroundColor;

        try
        {
            Console.ForegroundColor = GetStatusColor(entry.ReturnValue);
            Console.Write("[SqlLog] ");

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(entry.ApiName);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(" | status ");

            Console.ForegroundColor = GetStatusColor(entry.ReturnValue);
            Console.Write(entry.ReturnValue);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(" | ");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{ParseMsUsed(entry.MsUsed)} ms");

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($" | {schema}.{tableName}");

            if (!string.IsNullOrWhiteSpace(entry.ExecString))
            {
                Console.Write(" | ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(entry.ExecString);
            }

            var consoleError = !string.IsNullOrWhiteSpace(entry.UnexpectedError)
                ? entry.UnexpectedError
                : entry.ReturnValue >= 400
                    ? entry.ReturnBody
                    : null;

            if (!string.IsNullOrWhiteSpace(consoleError))
            {
                Console.Write(" | ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(consoleError);
            }

            Console.WriteLine();
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }

    private static ConsoleColor GetStatusColor(int statusCode)
        => statusCode >= 500
            ? ConsoleColor.Red
            : statusCode >= 400
                ? ConsoleColor.Yellow
                : ConsoleColor.Green;
}
