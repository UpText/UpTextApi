using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UpApi.Configuration;
using UpApi.Services;

namespace UpApi.Endpoints;

public static class SqlLogEndpoint
{
    public static IEndpointRouteBuilder MapSqlLog(this IEndpointRouteBuilder app)
    {
        app.MapGet("/SqlLog", HandleAsync)
            .WithName("SqlLog")
            .WithTags("BuiltIn")
            .WithSummary("Return filtered SQL log entries")
            .Produces<IReadOnlyList<SqlLogRow>>(StatusCodes.Status200OK, "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        app.MapGet("/SqlLog/Source", HandleSourceAsync)
            .WithName("SqlLogSource")
            .WithTags("BuiltIn")
            .WithSummary("Return T-SQL source for a stored procedure referenced by a SQL log entry")
            .Produces<SqlProcedureSource>(StatusCodes.Status200OK, "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status403Forbidden, contentType: "text/plain")
            .Produces(StatusCodes.Status404NotFound, contentType: "text/plain")
            .Produces(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        return app;
    }

    private static async Task<IResult> HandleSourceAsync(
        HttpRequest request,
        ISqlLog sqlLog,
        IServiceConfigResolver serviceConfigResolver,
        IOptions<ServiceConfigurations> serviceOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        string? service = null,
        string? procedure = null)
    {
        if (!HomeProtection.IsAuthorized(request))
        {
            return HomeProtection.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(procedure))
        {
            return Results.Text("procedure is required.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var sourceConnectionString = ResolveSourceConnectionString(
                service,
                procedure,
                serviceConfigResolver,
                serviceOptions.Value);
            var source = await sqlLog.ReadProcedureSourceAsync(procedure, sourceConnectionString, cancellationToken);
            if (source is null)
            {
                return Results.Text("Stored procedure source was not found.", "text/plain", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(source);
        }
        catch (ArgumentException ex)
        {
            return Results.Text(ex.Message, "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Text(ex.Message, "text/plain", statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex) when (ex.Message.Equals("SqlLog is not configured.", StringComparison.Ordinal))
        {
            return Results.Text(
                "SQL log is not configured.",
                "text/plain",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("SqlLogEndpoint").LogError(ex, "Failed to read source for procedure {Procedure}", procedure);
            return Results.Text("Failed to read stored procedure source.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string? ResolveSourceConnectionString(
        string? service,
        string procedure,
        IServiceConfigResolver serviceConfigResolver,
        ServiceConfigurations serviceConfigurations)
    {
        if (!string.IsNullOrWhiteSpace(service) &&
            serviceConfigResolver.TryGet(service, out var serviceConfiguration))
        {
            return serviceConfiguration.SqlConnectionString;
        }

        var schema = ExtractSchemaName(procedure);
        if (string.IsNullOrWhiteSpace(schema))
        {
            return null;
        }

        foreach (var configuredService in serviceConfigurations.Services)
        {
            if (string.Equals(configuredService.Value.SqlSchema, schema, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(configuredService.Key, schema, StringComparison.OrdinalIgnoreCase))
            {
                return configuredService.Value.SqlConnectionString;
            }
        }

        return null;
    }

    private static string? ExtractSchemaName(string procedure)
    {
        var trimmed = procedure.Trim().TrimEnd(';').TrimEnd();
        var separatorIndex = trimmed.LastIndexOf('.');
        if (separatorIndex <= 0)
        {
            return null;
        }

        return UnquoteSqlIdentifier(trimmed[..separatorIndex]);
    }

    private static string UnquoteSqlIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            return trimmed[1..^1].Replace("]]", "]", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        ISqlLog sqlLog,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        string? service = null,
        int? maxHours = null,
        int? aboveTime = null,
        [FromQuery(Name = "OnlySqlError")] bool? onlySqlError = null,
        [FromQuery(Name = "UserError")] bool? userError = null,
        [FromQuery(Name = "Today")] bool? today = null,
        [FromQuery(Name = "FromRow")] int fromRow = 0,
        [FromQuery(Name = "ToRow")] int toRow = 100,
        string? procedure = null,
        string sortBy = "time",
        string sortDirection = "desc")
    {
        if (!HomeProtection.IsAuthorized(request))
        {
            return HomeProtection.Unauthorized();
        }

        if (maxHours < 0)
        {
            return Results.Text("maxHours must be zero or greater.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        if (aboveTime < 0)
        {
            return Results.Text("aboveTime must be zero or greater.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        if (fromRow < 0)
        {
            return Results.Text("FromRow must be zero or greater.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        if (toRow < fromRow)
        {
            return Results.Text("ToRow must be greater than or equal to FromRow.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseSortColumn(sortBy, out var sortColumn))
        {
            return Results.Text(
                "sortBy must be one of: time, service, status, duration, server, sqlExecution.",
                "text/plain",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseSortDirection(sortDirection, out var direction))
        {
            return Results.Text(
                "sortDirection must be either asc or desc.",
                "text/plain",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var rows = await sqlLog.ReadAsync(
                service,
                maxHours,
                aboveTime,
                onlySqlError,
                userError,
                today,
                procedure,
                fromRow,
                toRow,
                sortColumn,
                direction,
                cancellationToken);

            return Results.Json(rows);
        }
        catch (InvalidOperationException ex) when (ex.Message.Equals("SqlLog is not configured.", StringComparison.Ordinal))
        {
            return Results.Text(
                "SQL log is not configured.",
                "text/plain",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("SqlLogEndpoint").LogError(ex, "Failed to read SQL log entries");
            return Results.Text("Failed to read SQL log entries.", "text/plain", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static bool TryParseSortColumn(string value, out SqlLogSortColumn sortColumn)
    {
        sortColumn = value.ToLowerInvariant() switch
        {
            "time" => SqlLogSortColumn.Time,
            "service" => SqlLogSortColumn.Service,
            "status" => SqlLogSortColumn.Status,
            "duration" => SqlLogSortColumn.Duration,
            "server" => SqlLogSortColumn.Server,
            "sqlexecution" => SqlLogSortColumn.SqlExecution,
            _ => default
        };

        return value.Equals("time", StringComparison.OrdinalIgnoreCase)
            || value.Equals("service", StringComparison.OrdinalIgnoreCase)
            || value.Equals("status", StringComparison.OrdinalIgnoreCase)
            || value.Equals("duration", StringComparison.OrdinalIgnoreCase)
            || value.Equals("server", StringComparison.OrdinalIgnoreCase)
            || value.Equals("sqlExecution", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSortDirection(string value, out SqlLogSortDirection sortDirection)
    {
        if (value.Equals("asc", StringComparison.OrdinalIgnoreCase))
        {
            sortDirection = SqlLogSortDirection.Asc;
            return true;
        }

        if (value.Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            sortDirection = SqlLogSortDirection.Desc;
            return true;
        }

        sortDirection = default;
        return false;
    }
}
