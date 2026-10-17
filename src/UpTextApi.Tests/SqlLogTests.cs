using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class SqlLogTests
{
    // An unsupported connection-string keyword fails locally, without contacting a server.
    private const string InvalidConnectionString = "UnsupportedSqlLogTestKeyword=value";
    private static readonly SqlLogEntry Entry = new("test-api", "12", 200, "{}", "{}", "", "", "");

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t")]
    public async Task WithMissingConnectionString_SkipsWritesAndRejectsReads(string? connectionString)
    {
        var logger = new RecordingLogger();
        var log = Create(new SqlLogOptions { ConnectionString = connectionString! }, logger);

        await log.EnsureTableAsync();
        await log.LogAsync(Entry);
        AssertUnavailable(log);
        Assert.That(logger.Entries, Is.Empty);
    }

    [TestCase("", "log")]
    [TestCase("dbo", "")]
    [TestCase(" ", "log")]
    [TestCase("dbo", " ")]
    [TestCase("dbo]; DROP TABLE log;--", "log")]
    [TestCase("dbo", "log]; DROP TABLE users;--")]
    [TestCase("dbo.other", "log")]
    [TestCase("dbo", "log-name")]
    public async Task WithUnsafeIdentifiers_SkipsWritesRejectsReadsAndLogsWarnings(string schema, string tableName)
    {
        var logger = new RecordingLogger();
        var log = Create(new SqlLogOptions
        {
            ConnectionString = InvalidConnectionString,
            Schema = schema,
            TableName = tableName
        }, logger);

        await log.EnsureTableAsync();
        await log.LogAsync(Entry);
        AssertUnavailable(log);

        Assert.That(logger.Entries, Is.Not.Empty);
        foreach (var entry in logger.Entries)
        {
            Assert.Multiple(() =>
            {
                Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
                Assert.That(entry.Message, Does.Contain("not valid SQL identifiers"));
                Assert.That(entry.Exception, Is.Null);
            });
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PurgeOlderThanAsync_WithNonPositiveRetention_ThrowsForRetention(int days)
    {
        var log = Create(new SqlLogOptions());

        var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await log.PurgeOlderThanAsync(TimeSpan.FromDays(days), 100));

        Assert.That(exception!.ParamName, Is.EqualTo("retention"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PurgeOlderThanAsync_WithNonPositiveBatchSize_ThrowsForBatchSize(int batchSize)
    {
        var log = Create(new SqlLogOptions());

        var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await log.PurgeOlderThanAsync(TimeSpan.FromDays(30), batchSize));

        Assert.That(exception!.ParamName, Is.EqualTo("batchSize"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    public void ReadProcedureSourceAsync_WithBlankProcedure_ThrowsForProcedure(string? procedure)
    {
        var log = Create(new SqlLogOptions());

        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await log.ReadProcedureSourceAsync(procedure!));

        Assert.That(exception!.ParamName, Is.EqualTo("procedure"));
    }

    [TestCase(";")]
    [TestCase(" ; ; \t;")]
    public void ReadProcedureSourceAsync_WithOnlyTerminators_ThrowsBeforeOpeningConnection(string procedure)
    {
        var log = Create(new SqlLogOptions { ConnectionString = InvalidConnectionString });

        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await log.ReadProcedureSourceAsync(procedure));

        Assert.That(exception!.ParamName, Is.EqualTo("procedure"));
    }

    [Test]
    public void ReadAsync_WithUnknownSortColumn_ThrowsBeforeOpeningConnection()
    {
        var log = Create(new SqlLogOptions { ConnectionString = InvalidConnectionString });

        var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Read(log, (SqlLogSortColumn)999));

        Assert.That(exception!.ParamName, Is.EqualTo("sortColumn"));
    }

    [Test]
    public async Task EnsureTableAsync_WithConnectionFailure_LogsWarningWithoutThrowing()
    {
        var logger = new RecordingLogger();
        var log = Create(new SqlLogOptions { ConnectionString = InvalidConnectionString }, logger);

        await log.EnsureTableAsync();

        AssertFailureWarning(logger, "Failed to initialize SQL log table dbo.log");
    }

    [Test]
    public async Task LogAsync_WithConnectionFailure_LogsWarningWithoutThrowing()
    {
        var logger = new RecordingLogger();
        var log = Create(new SqlLogOptions { ConnectionString = InvalidConnectionString }, logger);

        await log.LogAsync(Entry);

        AssertFailureWarning(logger, "Failed to write SQL log entry for test-api");
    }

    private static SqlLog Create(SqlLogOptions options, RecordingLogger? logger = null) =>
        new(Options.Create(options), logger ?? new RecordingLogger());

    private static Task<IReadOnlyList<SqlLogRow>> Read(SqlLog log,
        SqlLogSortColumn sortColumn = SqlLogSortColumn.Time) =>
        log.ReadAsync(null, null, null, null, null, null, null, 0, 99,
            sortColumn, SqlLogSortDirection.Desc);

    private static void AssertUnavailable(SqlLog log)
    {
        Assert.That(async () => await Read(log),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("SqlLog is not configured."));
        Assert.That(async () => await log.ReadServiceStatusAsync("test-api"),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("SqlLog is not configured."));
        Assert.That(async () => await log.ReadProcedureSourceAsync("dbo.test"),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("SqlLog is not configured."));
        Assert.That(async () => await log.PurgeOlderThanAsync(TimeSpan.FromDays(30), 100),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("SqlLog is not configured."));
    }

    private static void AssertFailureWarning(RecordingLogger logger, string message)
    {
        Assert.That(logger.Entries, Has.Count.EqualTo(1));
        var entry = logger.Entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Is.EqualTo(message));
            Assert.That(entry.Exception, Is.InstanceOf<ArgumentException>());
        });
    }

    private sealed class RecordingLogger : ILogger<SqlLog>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
