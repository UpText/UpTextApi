namespace UpApi.Endpoints;

public static class Favicon
{
    public static IEndpointRouteBuilder MapFavicon(this IEndpointRouteBuilder app)
    {
        app.MapGet("/favicon.svg", () => Results.Content(UpApiBrand.IconSvg, "image/svg+xml"))
            .ExcludeFromDescription();

        return app;
    }
}
