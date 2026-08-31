using UpApi.Services;

namespace UpApi.Endpoints;

public static class ServiceLogStatus
{
    public static IEndpointRouteBuilder MapServiceLogStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/ServiceLogStatus", HandleAsync)
            .WithName("ServiceLogStatus")
            .WithTags("BuiltIn")
            .WithSummary("Return SQL log totals for a service")
            .Produces<ServiceLogStatusResult>(StatusCodes.Status200OK, "application/json")
            .Produces(StatusCodes.Status400BadRequest, contentType: "text/plain")
            .Produces(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain")
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        string? service,
        ISqlLog sqlLog,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!HomeProtection.IsAuthorized(request))
        {
            return HomeProtection.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(service))
        {
            return Results.Text("service is required.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var status = await sqlLog.ReadServiceStatusAsync(service, cancellationToken);
            return Results.Json(status);
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
            loggerFactory.CreateLogger("ServiceLogStatus").LogError(
                ex,
                "Failed to read SQL log status for service {Service}",
                service);

            return Results.Text(
                "Failed to read SQL log status.",
                "text/plain",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
