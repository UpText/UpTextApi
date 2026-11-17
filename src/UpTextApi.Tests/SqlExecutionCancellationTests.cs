using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using UpTextApi.Configuration;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class SqlExecutionCancellationTests
{
    [Test]
    public async Task CanceledRequest_DoesNotResolveServiceOpenConnectionOrLog()
    {
        var resolver = new Resolver();
        var log = new RecordingLog();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var executor = new SqlWebApiExecutor(resolver, cache, new ConfigurationBuilder().Build(),
            log, NullLogger<SqlWebApiExecutor>.Instance, new TestEnvironment());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext();

        var result = await executor.ExecuteAsync(context.Request, "crmapi", "deals", null, null,
            cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(((IStatusCodeHttpResult)result).StatusCode, Is.EqualTo(499));
            Assert.That(resolver.Calls, Is.Zero);
            Assert.That(log.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ConnectionFailure_ReturnsServiceUnavailableWithoutOpeningLoggingConnection()
    {
        // Reserve then release a local port with no SQL listener; no database is required.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var resolver = new Resolver
        {
            Value = new ServiceConfiguration
            {
                SqlSchema = "crmapi",
                SqlConnectionString = $"Server=127.0.0.1,{port};User ID=test;Password=test;Connect Timeout=1;Pooling=false;Encrypt=false"
            }
        };
        var log = new RecordingLog();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var executor = new SqlWebApiExecutor(resolver, cache, new ConfigurationBuilder().Build(),
            log, NullLogger<SqlWebApiExecutor>.Instance, new TestEnvironment());
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();

        var result = await executor.ExecuteAsync(context.Request, "crmapi", "deals", null, null,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(log.Calls, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadTableAsync_PreservesRowsTypesNullsAndBinary(bool firstRowAvailable)
    {
        var source = new DataTable();
        source.Columns.Add("id", typeof(int));
        source.Columns.Add("name", typeof(string));
        source.Columns.Add("data", typeof(byte[]));
        source.Rows.Add(1, DBNull.Value, new byte[] { 1, 2 });
        source.Rows.Add(2, "second", new byte[] { 3 });
        using var reader = source.CreateDataReader();
        if (firstRowAvailable) await reader.ReadAsync();

        var table = await SqlWebApiExecutor.ReadTableAsync(reader, CancellationToken.None, firstRowAvailable);

        Assert.Multiple(() =>
        {
            Assert.That(table.Rows.Count, Is.EqualTo(2));
            Assert.That(table.Columns["id"]!.DataType, Is.EqualTo(typeof(int)));
            Assert.That(table.Rows[0]["name"], Is.EqualTo(DBNull.Value));
            Assert.That(table.Rows[0]["data"], Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(table.Rows[1]["name"], Is.EqualTo("second"));
        });
    }

    [Test]
    public void ReadTableAsync_ObservesCancellation()
    {
        using var reader = new DataTable().CreateDataReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await SqlWebApiExecutor.ReadTableAsync(reader, cancellation.Token));
    }

    private sealed class Resolver : IServiceConfigResolver
    {
        public int Calls { get; private set; }
        public ServiceConfiguration? Value { get; init; }
        public bool TryGet(string name, out ServiceConfiguration configuration)
        {
            Calls++;
            configuration = Value ?? new ServiceConfiguration();
            return Value is not null;
        }
    }

    private sealed class RecordingLog : ISqlLog
    {
        public int Calls { get; private set; }
        public Task EnsureTableAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> PurgeOlderThanAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceLogStatusResult> ReadServiceStatusAsync(string service, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SqlProcedureSource?> ReadProcedureSourceAsync(string procedure, string? connectionString = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SqlLogRow>> ReadAsync(string? service, int? maxHours, int? aboveTime,
            bool? onlySqlError, bool? userError, bool? today, string? procedure, int fromRow, int toRow,
            SqlLogSortColumn sortColumn, SqlLogSortDirection sortDirection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogAsync(SqlLogEntry entry, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
