using Microsoft.AspNetCore.Http.HttpResults;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Phone;
using MMDb.Contracts.Scans;
using MMDb.Contracts.Sync;
using MMDb.Contracts.Wishlist;
using MMDb.Domain.Devices;
using MMDb.Infrastructure.Metadata;
using MMDb.Infrastructure.Persistence;
using MMDb.Server.Phone;

namespace MMDb.Server.Http;

/// <summary>
/// The collection over HTTP for paired phones: <see cref="ICollectionApi"/> call for call under <c>/api/v1</c>.
/// Everything but <c>health</c> and <c>pair</c> needs the token a phone got when it paired.
/// </summary>
public static class ShelfEndpoints
{
    private const string DeviceItem = "shelf.device";

    public static IServiceCollection AddShelfApi(this IServiceCollection services) => services
        .AddProblemDetails()
        .ConfigureHttpJsonOptions(json => ShelfJson.Configure(json.SerializerOptions));

    public static IEndpointRouteBuilder MapShelfApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/health", () => new HealthResponse("ok"));
        api.MapPost("/pair", PairAsync);

        var paired = api.MapGroup("").AddEndpointFilter(RequireDeviceAsync);
        paired.MapGet("/session", (HttpContext http, PhoneServer server) => new SessionResponse(server.PcName, ((PairedDevice)http.Items[DeviceItem]!).Name));

        // A paired phone looks things up itself, away from home too, so it gets what the PC uses for that.
        paired.MapGet("/keys", (TmdbSettings tmdb, MetronSettings metron) => new LookupKeysResponse(Blank(tmdb.ApiKey), Blank(metron.Token)));

        paired.MapGet("/movies", (string? search, MovieFormatKind? format, string? genre, int? decade, string? director, MovieSortOrder? sort, ICollectionApi api, CancellationToken ct) =>
            api.ListMoviesAsync(new() { Search = search, Format = format, Genre = genre, Decade = decade, Director = director, Sort = sort ?? MovieSortOrder.RecentlyAdded }, ct));
        paired.MapGet("/movies/lookup", (string q, ICollectionApi api, CancellationToken ct) => api.LookupMovieAsync(q, ct));
        paired.MapGet("/movies/{id:guid}", async Task<Results<Ok<MovieDto>, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.GetMovieAsync(id, ct) is { } movie ? TypedResults.Ok(movie) : TypedResults.NotFound());
        paired.MapPost("/movies", async (SaveMovieRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.AddMovieAsync(request, ct), r => r.Status));
        paired.MapPut("/movies/{id:guid}", async (Guid id, int version, SaveMovieRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.UpdateMovieAsync(id, version, request, ct), r => r.Status));
        paired.MapDelete("/movies/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.DeleteMovieAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound());

        paired.MapGet("/comics", (string? search, ComicShelfKind? shelf, string? publisher, ComicSortOrder? sort, ICollectionApi api, CancellationToken ct) =>
            api.ListComicsAsync(new() { Search = search, Shelf = shelf ?? ComicShelfKind.Series, Publisher = publisher, Sort = sort ?? ComicSortOrder.Series }, ct));
        paired.MapGet("/comics/lookup", (string q, ICollectionApi api, CancellationToken ct) => api.LookupComicAsync(q, ct));
        paired.MapGet("/comics/{id:guid}", async Task<Results<Ok<ComicDto>, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.GetComicAsync(id, ct) is { } comic ? TypedResults.Ok(comic) : TypedResults.NotFound());
        paired.MapPost("/comics", async (SaveComicRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.AddComicAsync(request, ct), r => r.Status));
        paired.MapPut("/comics/{id:guid}", async (Guid id, int version, SaveComicRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.UpdateComicAsync(id, version, request, ct), r => r.Status));
        paired.MapDelete("/comics/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.DeleteComicAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound());

        paired.MapGet("/wishlist", (string? search, ICollectionApi api, CancellationToken ct) => api.ListWishlistAsync(search, ct));
        paired.MapPost("/wishlist/movies", async (WantMovieRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.WantMovieAsync(request, ct), r => r.Status));
        paired.MapGet("/wishlist/movies/{id:guid}", async Task<Results<Ok<WishlistMovieDto>, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.GetWishlistMovieAsync(id, ct) is { } movie ? TypedResults.Ok(movie) : TypedResults.NotFound());
        paired.MapPut("/wishlist/movies/{id:guid}/format", async (Guid id, ChangeWishFormatRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.ChangeWishlistFormatAsync(id, request.Format, ct), r => r.Status));
        paired.MapGet("/wishlist/comics/{id:guid}", async Task<Results<Ok<WishlistComicDto>, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.GetWishlistComicAsync(id, ct) is { } comic ? TypedResults.Ok(comic) : TypedResults.NotFound());
        paired.MapDelete("/wishlist/movies/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.RemoveWishlistMovieAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound());
        paired.MapGet("/wishlist/movies/{id:guid}/lookup", async Task<Results<Ok<MovieLookupResponse>, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.LookupWishlistMovieAsync(id, ct) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound());
        paired.MapPost("/wishlist/comics", async (WantComicRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.WantComicAsync(request, ct), r => r.Status));
        paired.MapPost("/wishlist/comics/missing", async (WantMissingRequest request, ICollectionApi api, CancellationToken ct) =>
            Saved(await api.WantMissingIssuesAsync(request, ct), r => r.Status));
        paired.MapDelete("/wishlist/comics/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.RemoveWishlistComicAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound());
        paired.MapGet("/wishlist/comics/{id:guid}/lookup", async Task<Results<Ok<ComicLookupResponse>, NotFound>> (Guid id, ICollectionApi api, CancellationToken ct) =>
            await api.LookupWishlistComicAsync(id, ct) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound());

        // A phone keeps its own copy of the collection: it compares keys and stamps, then asks for the rows it lacks.
        paired.MapGet("/sync", async (CollectionCopy copy, CancellationToken ct) =>
            new SyncManifest(await copy.SchemaAsync(ct), [.. (await copy.ListAsync(ct)).Select(t => new SyncTable(t.Name, t.Stamps))]));
        paired.MapPost("/sync/rows", async Task<Results<Ok<SyncRowsResponse>, BadRequest>> (SyncRowsRequest request, CollectionCopy copy, CancellationToken ct) =>
            CollectionCopy.Tables.Contains(request.Table)
                ? TypedResults.Ok(new SyncRowsResponse([.. (await copy.ReadAsync(request.Table, request.Keys, ct)).Select(r => new SyncRow(r.Key, r.Values, r.Parts))]))
                : TypedResults.BadRequest());

        paired.MapPost("/scans", (ScanRequest request, ICollectionApi api, CancellationToken ct) => api.LookupScanAsync(request, ct));
        paired.MapGet("/covers/{name}", CoverAsync);
        return app;
    }

    private static string? Blank(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

    private static async Task<IResult> PairAsync(PairRequest request, DeviceGate gate, PhoneServer server, CancellationToken cancellationToken) =>
        await gate.PairAsync(request.Code, request.DeviceName, cancellationToken) is { } pairing
            ? TypedResults.Ok(new PairResponse(pairing.Token, pairing.Device.Id, server.PcName))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "That pairing code isn't valid.",
                detail: "It was mistyped, used already, or has expired. Use the code Shelf shows on the PC now.");

    private static async ValueTask<object?> RequireDeviceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : null;
        if (await http.RequestServices.GetRequiredService<DeviceGate>().AuthenticateAsync(token, http.RequestAborted) is not { } device)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "This phone isn't paired with the PC.",
                detail: "Pair it from Settings in Shelf on the PC.");
        }

        http.Items[DeviceItem] = device;
        return await next(context);
    }

    // The body is the same response the PC's screens get; the status says how it went for anything else.
    private static JsonHttpResult<T> Saved<T>(T response, Func<T, SaveStatus> status) => TypedResults.Json(response, statusCode: status(response) switch
    {
        SaveStatus.Saved => StatusCodes.Status200OK,
        SaveStatus.Invalid => StatusCodes.Status400BadRequest,
        SaveStatus.NotFound => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status409Conflict,
    });

    private static async Task<IResult> CoverAsync(string name, string? size, ICoverStore covers, HttpContext http, CancellationToken cancellationToken)
    {
        if (await covers.ReadAsync(name, cancellationToken) is not { } image)
        {
            return TypedResults.NotFound();
        }

        // A phone keeps a copy of every cover, so it asks for them smaller.
        if (size == "medium")
        {
            image = CoverSizes.Medium(image, name);
        }

        // A cover's name changes whenever its picture does, so the phone can keep it for good.
        http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return TypedResults.Bytes(image, Path.GetExtension(name).ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".WEBP" => "image/webp",
            ".GIF" => "image/gif",
            _ => "image/jpeg",
        });
    }
}
