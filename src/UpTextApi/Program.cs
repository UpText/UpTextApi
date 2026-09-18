using UpTextApi.Endpoints;
using UpTextApi.Configuration;
using UpTextApi.Services;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var secretResolver = SecretResolver.Default;

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi("swagger", options =>
{
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        var relativePath = context.Description.RelativePath;
        if (!string.Equals(relativePath, "SqlGenerator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(relativePath, "{service}/sql-generator", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(relativePath, "swa/{service}/sql-generator", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var httpVerbParameter = operation.Parameters?.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "http-verb", StringComparison.Ordinal));
        if (httpVerbParameter?.Schema is not OpenApiSchema schema)
        {
            return Task.CompletedTask;
        }

        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Enum =
        [
            JsonValue.Create("all"),
            JsonValue.Create("get"),
            JsonValue.Create("put"),
            JsonValue.Create("post"),
            JsonValue.Create("delete")
        ];

        return Task.CompletedTask;
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddMemoryCache();
builder.Services.AddHealthChecks()
    .AddCheck<ConfigurationHealthCheck>("configuration", tags: ["ready"])
    .AddCheck<SqlServicesHealthCheck>("sql", tags: ["ready"]);
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection(CorsOptions.SectionName)
            .Get<CorsOptions>()?
            .AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray() ?? [];

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Range");
    });
});
builder.Services.AddSingleton<ISecretResolver>(secretResolver);
builder.Services.Configure<ServiceConfigurations>(options =>
{
    builder.Configuration.GetSection(ServiceConfigurations.SectionName).Bind(options.Services);
    foreach (var service in options.Services.Values)
    {
        service.SqlConnectionString = secretResolver.Resolve(service.SqlConnectionString).Trim();
    }
});
builder.Services.Configure<SqlLogOptions>(options =>
{
    builder.Configuration.GetSection(SqlLogOptions.SectionName).Bind(options);
    options.ConnectionString = secretResolver.Resolve(options.ConnectionString).Trim();
});
builder.Services.AddSingleton<IServiceConfigResolver, ServiceConfigResolver>();
builder.Services.AddSingleton<ISqlWebApiExecutor, SqlWebApiExecutor>();
builder.Services.AddSingleton<ISqlLog, SqlLog>();

var sqlLogConnectionString = builder.Configuration[$"{SqlLogOptions.SectionName}:ConnectionString"];
if (!string.IsNullOrWhiteSpace(sqlLogConnectionString))
{
    builder.Services.AddHostedService<SqlLogCleanupService>();
}

var app = builder.Build();
await app.Services.GetRequiredService<ISqlLog>().EnsureTableAsync();

app.MapOpenApi("/{documentName}.json");

var corsOptions = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
var hasConfiguredCorsOrigins = corsOptions.AllowedOrigins.Any(origin => !string.IsNullOrWhiteSpace(origin));

if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

if (hasConfiguredCorsOrigins)
{
    app.UseCors("LocalFrontend");
}

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.Value?.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase) == true &&
        !HomeProtection.IsAuthorized(context.Request))
    {
        await HomeProtection.Unauthorized().ExecuteAsync(context);
        return;
    }

    await next(context);
});

app.MapPing();
app.MapHealth();
app.MapFavicon();
app.MapHome();
app.MapServerTime();
app.MapServiceLogStatus();
app.MapSqlGenFunc();
app.MapSqlGeneratorUi();
app.MapSqlImageFunc();
app.MapSqlLog();
app.MapSqlLogUi();
app.MapSqlWebApi();
app.MapSqlWebSiteDefinition();
app.MapServiceOpenApi();
app.MapSwaggerUi();

app.Run();
