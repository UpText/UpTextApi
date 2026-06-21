using Microsoft.AspNetCore.Mvc;
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
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        return app;
    }

    private static async Task<IResult> HandleAsync(
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
