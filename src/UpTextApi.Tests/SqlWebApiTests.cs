using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UpTextApi.Endpoints;
using UpTextApi.Services;

namespace UpTextApi.Tests;

public class SqlWebApiTests
{
    private static readonly string[] RouteNames =
        ["SqlWebApiRoot", "SqlWebApiRootWithId", "SqlWebApi", "SqlWebApiWithId"];

    [Test]
    public async Task MapSqlWebApi_RegistersFourNamedRoutesWithExpectedMethodsAndMetadata()
    {
        await using var app = CreateApp(new RecordingExecutor());
        var endpoints = Endpoints(app);

        Assert.That(endpoints, Has.Count.EqualTo(4));
        Assert.That(endpoints.Select(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName),
            Is.EquivalentTo(RouteNames));
        Assert.That(endpoints.Select(e => e.RoutePattern.RawText), Is.EquivalentTo(new[]
        {
            "/{service}/{resource}", "/{service}/{resource}/{id}",
            "/swa/{service}/{resource}", "/swa/{service}/{resource}/{id}"
        }));
        foreach (var endpoint in endpoints)
        {
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods,
                    Is.EquivalentTo(new[] { "GET", "POST", "PUT", "DELETE", "OPTIONS" }));
                Assert.That(endpoint.Metadata.GetMetadata<ITagsMetadata>()!.Tags, Does.Contain("SqlWebApi"));
                Assert.That(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()!.Summary,
                    Is.EqualTo("Execute SQL-backed REST endpoints"));
            });
        }
    }

    [TestCaseSource(nameof(RouteNames))]
    public async Task Options_ReturnsNoContentAndAllowHeaderWithoutCallingExecutor(string routeName)
    {
        var executor = new RecordingExecutor();
        await using var app = CreateApp(executor);
        var context = Context(app, "OPTIONS", routeName);

        await Endpoint(app, routeName).RequestDelegate!(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
            Assert.That(context.Response.Headers.Allow.ToString(), Is.EqualTo("GET, POST, PUT, DELETE, OPTIONS"));
            Assert.That(context.Response.Body.Length, Is.Zero);
            Assert.That(executor.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task Requests_ForwardBoundValuesAndReturnExecutorResponse(
        [ValueSource(nameof(RouteNames))] string routeName,
        [Values("GET", "POST", "PUT", "DELETE")] string method)
    {
        var executor = new RecordingExecutor();
        await using var app = CreateApp(executor);
        using var cancellation = new CancellationTokenSource();
        var context = Context(app, method, routeName);
        context.RequestAborted = cancellation.Token;
        context.Request.QueryString = new QueryString("?details=line%20items");

        await Endpoint(app, routeName).RequestDelegate!(context);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Calls, Is.EqualTo(1));
            Assert.That(executor.Request, Is.SameAs(context.Request));
            Assert.That(executor.Service, Is.EqualTo("crm"));
            Assert.That(executor.Resource, Is.EqualTo("orders"));
            Assert.That(executor.Id, Is.EqualTo(routeName.EndsWith("WithId") ? "42" : null));
            Assert.That(executor.Details, Is.EqualTo("line items"));
            Assert.That(executor.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(context.Response.StatusCode, Is.EqualTo(202));
            Assert.That(context.Response.Headers["X-Test"].ToString(), Is.EqualTo("executor"));
        });
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("executor response"));
    }

    [Test]
    public async Task Request_WithoutOptionalQueryValues_ForwardsNulls()
    {
        var executor = new RecordingExecutor();
        await using var app = CreateApp(executor);
        var context = Context(app, "GET", "SqlWebApiRoot");

        await Endpoint(app, "SqlWebApiRoot").RequestDelegate!(context);

        Assert.Multiple(() =>
        {
            Assert.That(executor.Calls, Is.EqualTo(1));
            Assert.That(executor.Id, Is.Null);
            Assert.That(executor.Details, Is.Null);
        });
    }

    private static WebApplication CreateApp(RecordingExecutor executor)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ContentRootPath = Path.GetTempPath(),
            Args = []
        });
        builder.Services.AddSingleton<ISqlWebApiExecutor>(executor);
        var app = builder.Build();
        Assert.That(app.MapSqlWebApi(), Is.SameAs(app));
        return app;
    }

    private static List<RouteEndpoint> Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().ToList();

    private static RouteEndpoint Endpoint(WebApplication app, string name) =>
        Endpoints(app).Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);

    private static DefaultHttpContext Context(WebApplication app, string method, string routeName)
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = method;
        context.Request.RouteValues["service"] = "crm";
        context.Request.RouteValues["resource"] = "orders";
        if (routeName.EndsWith("WithId"))
            context.Request.RouteValues["id"] = "42";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class RecordingExecutor : ISqlWebApiExecutor
    {
        public int Calls { get; private set; }
        public HttpRequest? Request { get; private set; }
        public string? Service { get; private set; }
        public string? Resource { get; private set; }
        public string? Id { get; private set; }
        public string? Details { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<IResult> ExecuteAsync(HttpRequest request, string service, string resource,
            string? id, string? details, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Service = service;
            Resource = resource;
            Id = id;
            Details = details;
            CancellationToken = cancellationToken;
            return Task.FromResult<IResult>(new SqlWebApiHttpResult(202, text: "executor response",
                headers: new Dictionary<string, string> { ["X-Test"] = "executor" }));
        }
    }
}
