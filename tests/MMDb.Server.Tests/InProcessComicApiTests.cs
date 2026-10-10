using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MMDb.Application.Metadata;
using MMDb.Contracts;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Domain.Comics;
using MMDb.Infrastructure;
using MMDb.Testing;

namespace MMDb.Server.Tests;

/// <summary>Comics through the UI's contracts, end to end over the real application and database.</summary>
public sealed class InProcessComicApiTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemorySqlite _sqlite = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeOnline _online = new();
    private ServiceProvider _services = null!;

    private ICollectionApi Api => _services.GetRequiredService<ICollectionApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SaveComicRequest Saga7 => new()
    {
        Series = "Saga",
        SeriesYear = 2012,
        Number = "7",
        Title = "Chapter Seven",
        Variant = "Cover B",
        Printing = 2,
        Publisher = "Image",
        CoverDate = new DateOnly(2012, 11, 1),
        Writers = ["Brian K. Vaughan"],
        Artists = ["Fiona Staples"],
        Summary = "Marko and Alana arrive on Quietus.",
        Barcode = "709853017448 00722",
        MetronId = 4521,
        MetronSeriesId = 1234,
        IsKeyIssue = true,
        KeyNote = "1st The Will",
        CoverSource = "https://static.metron.cloud/media/issue/saga-7.jpg",
    };

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection()
            .AddShelf(FakeOnline.Settings(_sqlite), new Uri("https://covers.test/"))
            .AddSingleton<TimeProvider>(_clock);
        _services = _online.ReplaceIn(services).BuildServiceProvider();
        await _services.MigrateShelfDatabaseAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _sqlite.Dispose();
    }

    [Fact]
    public async Task An_added_comic_comes_back_with_every_field_and_its_run()
    {
        _online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["6", "7", "8"]));

        var response = await Api.AddComicAsync(Saga7, Ct);

        response.Status.ShouldBe(SaveStatus.Saved);
        var comic = (await Api.GetComicAsync(response.Comic!.Id, Ct)).ShouldNotBeNull();
        comic.ShouldSatisfyAllConditions(
            c => c.DisplayName.ShouldBe("Saga #7"),
            c => c.Format.ShouldBe(ComicFormatKind.SingleIssue),
            c => c.Variant.ShouldBe("Cover B"),
            c => c.Printing.ShouldBe(2),
            c => c.CoverDate.ShouldBe(new DateOnly(2012, 11, 1)),
            c => c.Writers.ShouldBe(["Brian K. Vaughan"]),
            c => c.Barcode.ShouldBe("70985301744800722"),
            c => c.KeyNote.ShouldBe("1st The Will"),
            c => c.CoverSource.ShouldBe(Saga7.CoverSource),
            c => c.CoverUrl!.AbsoluteUri.ShouldStartWith($"https://covers.test/{c.Id:N}-"),
            c => c.CreatedAt.ShouldBe(Start),
            c => c.Version.ShouldBe(1));
        var run = comic.Run.ShouldNotBeNull();
        run.Title.ShouldBe("Saga (2012)");
        run.KnownCount.ShouldBe(3);
        run.Entries.ShouldHaveSingleItem().Copies.ShouldHaveSingleItem().Id.ShouldBe(comic.Id);
    }

    [Fact]
    public async Task The_library_lists_runs_and_shelves_with_the_header_counts()
    {
        await Api.AddComicAsync(Saga7, Ct);
        await Api.AddComicAsync(new SaveComicRequest { Format = ComicFormatKind.Hardcover, Title = "Saga Deluxe Edition", Isbn = "9781632154163" }, Ct);

        var series = await Api.ListComicsAsync(new(), Ct);
        var collected = await Api.ListComicsAsync(new() { Shelf = ComicShelfKind.Collected }, Ct);

        series.Runs.ShouldHaveSingleItem().Name.ShouldBe("Saga");
        series.Facets.ShouldSatisfyAllConditions(f => f.IssueCount.ShouldBe(1), f => f.CollectedCount.ShouldBe(1), f => f.KeyIssueCount.ShouldBe(1));
        collected.Comics.ShouldHaveSingleItem().Format.ShouldBe(ComicFormatKind.Hardcover);
    }

    [Fact]
    public async Task Rejected_input_names_the_fields()
    {
        var response = await Api.AddComicAsync(new SaveComicRequest { Series = "Saga", Number = "7", Isbn = "12345" }, Ct);

        response.Status.ShouldBe(SaveStatus.Invalid);
        response.Errors.ShouldHaveSingleItem().Field.ShouldBe("Isbn");
    }

    [Fact]
    public async Task An_unknown_format_is_rejected_not_thrown()
    {
        var response = await Api.AddComicAsync(new SaveComicRequest { Format = (ComicFormatKind)99, Title = "?" }, Ct);

        response.Errors.ShouldHaveSingleItem().Field.ShouldBe("Format");
    }

    [Fact]
    public async Task The_same_copy_twice_comes_back_as_already_owned()
    {
        var first = await Api.AddComicAsync(Saga7, Ct);

        var second = await Api.AddComicAsync(Saga7, Ct);

        second.Status.ShouldBe(SaveStatus.AlreadyOwned);
        second.Comic!.Id.ShouldBe(first.Comic!.Id);
    }

    [Fact]
    public async Task Editing_and_deleting_go_through_the_version()
    {
        var saved = (await Api.AddComicAsync(Saga7, Ct)).Comic!;

        var edited = await Api.UpdateComicAsync(saved.Id, saved.Version, Saga7 with { Variant = "Cover C" }, Ct);
        var stale = await Api.UpdateComicAsync(saved.Id, saved.Version, Saga7, Ct);

        edited.Comic!.Variant.ShouldBe("Cover C");
        stale.Status.ShouldBe(SaveStatus.Conflict);
        (await Api.DeleteComicAsync(saved.Id, Ct)).ShouldBeTrue();
        (await Api.GetComicAsync(saved.Id, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_lookup_comes_back_ready_to_save()
    {
        _online.Metron.Add(TestComics.Saga7);

        var response = await Api.LookupComicAsync("saga 7", Ct);

        response.Status.ShouldBe(LookupStatus.Found);
        var candidate = response.Candidates.ShouldHaveSingleItem();
        candidate.DisplayName.ShouldBe("Saga #7");
        candidate.CoverUrl.ShouldBe(new Uri(TestComics.Saga7.CoverUrl!));
        (await Api.AddComicAsync(candidate.Details, Ct)).Status.ShouldBe(SaveStatus.Saved);
    }

    [Theory]
    [InlineData(MetadataProblem.NotConfigured, LookupStatus.NotConfigured)]
    [InlineData(MetadataProblem.KeyRejected, LookupStatus.KeyRejected)]
    [InlineData(MetadataProblem.Busy, LookupStatus.Busy)]
    [InlineData(MetadataProblem.Unavailable, LookupStatus.Unavailable)]
    public async Task Lookup_problems_become_statuses(MetadataProblem problem, LookupStatus status)
    {
        _online.Metron.FailWith = problem;

        var response = await Api.LookupComicAsync("saga 7", Ct);

        response.Status.ShouldBe(status);
        response.FailedService.ShouldBe("Metron");
    }
}
