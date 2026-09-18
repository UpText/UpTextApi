namespace UpTextApi.Endpoints;

public static class Favicon
{
    public static IEndpointRouteBuilder MapFavicon(this IEndpointRouteBuilder app)
    {
        app.MapGet("/favicon.svg", () => Results.Content(UpTextApiBrand.IconSvg, "image/svg+xml"))
            .ExcludeFromDescription();

        return app;
    }
}
