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
            .Produces(StatusCodes.Status500InternalServerError, contentType: "text/plain");

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string? service,
        ISqlLog sqlLog,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service))
        {
            return Results.Text("service is required.", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var status = await sqlLog.ReadServiceStatusAsync(service, cancellationToken);
            return Results.Json(status);
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
