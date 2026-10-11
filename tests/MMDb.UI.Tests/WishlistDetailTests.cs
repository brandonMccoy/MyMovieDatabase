using Bunit;
using MMDb.Application.Metadata;
using MMDb.Contracts.Comics;
using MMDb.Contracts.Movies;
using MMDb.Contracts.Wishlist;
using MMDb.Domain.Comics;
using MMDb.Testing;
using MMDb.UI.Pages;

namespace MMDb.UI.Tests;

/// <summary>A wishlist item's own page: read-only, except for the format a film is wanted on.</summary>
public sealed class WishlistDetailTests : ShelfTestContext
{
    public WishlistDetailTests()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb).Add(TestMovies.Heat1986OnTmdb);
        Online.Metron.Add(TestComics.Saga7);
        Online.Books.Add(TestComics.SagaVolume1);
    }

    private async Task<WishlistMovieDto> WantHeatAsync(MovieFormatKind format = MovieFormatKind.Uhd4K, int? tmdbId = 949) =>
        (await Api.WantMovieAsync(new()
        {
            Title = "Heat",
            Year = 1995,
            Directors = ["Michael Mann"],
            TmdbId = tmdbId,
            TrailerYouTubeKey = "0xbBLJ1WGwQ",
            PosterPath = "/heat.jpg",
            Format = format,
        }, Ct)).Movie!;

    private async Task<WishlistComicDto> WantAsync(ComicDetails comic) =>
        (await Api.WantComicAsync(new()
        {
            Format = (ComicFormatKind)(int)comic.Format,
            Series = comic.Series,
            SeriesYear = comic.SeriesYear,
            Number = comic.Number,
            Title = comic.Title,
            Publisher = comic.Publisher,
            Isbn = comic.Isbn,
            MetronId = comic.MetronId,
            MetronSeriesId = comic.MetronSeriesId,
            CoverSource = comic.CoverUrl,
        }, Ct)).Comic!;

    private IRenderedComponent<WishlistMovieDetail> RenderFilm(Guid id)
    {
        Navigation.NavigateTo($"wishlist/movies/{id}");
        var page = Render<WishlistMovieDetail>(p => p.Add(x => x.Id, id));
        page.WaitForElement(".detail, .empty");
        return page;
    }

    private IRenderedComponent<WishlistComicDetail> RenderComic(Guid id)
    {
        Navigation.NavigateTo($"wishlist/comics/{id}");
        var page = Render<WishlistComicDetail>(p => p.Add(x => x.Id, id));
        page.WaitForElement(".detail, .empty");
        return page;
    }

    private static string[] FormatRows<TPage>(IRenderedComponent<TPage> page)
        where TPage : Microsoft.AspNetCore.Components.IComponent =>
        [.. page.FindAll(".wanted-format .fmt-toggle").Select(b => $"{Text(b.QuerySelector(".fmt")!)}:{Text(b.QuerySelector(".small")!)}")];

    // The badge of the format wanted and the upgrade tag, above the title.
    private static string[] Tags(IRenderedComponent<WishlistMovieDetail> page) => [.. page.Find(".fmts").Children.Select(Text)];

    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    [Fact]
    public async Task Wishlist_rows_open_the_items_own_page()
    {
        var heat = await WantHeatAsync();
        var saga = await WantAsync(TestComics.Saga7);

        Navigation.NavigateTo("wishlist");
        var films = Render<WishlistPage>();
        films.WaitForElement(".wl-row");
        films.Find(".wl-title a").GetAttribute("href").ShouldBe($"wishlist/movies/{heat.Id}");
        films.Find("a.wl-cover").GetAttribute("href").ShouldBe($"wishlist/movies/{heat.Id}");
        films.Find("a.wl-cover").GetAttribute("aria-label").ShouldBe("Heat");

        Navigation.NavigateTo("wishlist?tab=comics");
        var comics = Render<WishlistPage>();
        comics.WaitForElement(".wl-row");
        comics.Find(".wl-title a").GetAttribute("href").ShouldBe($"wishlist/comics/{saga.Id}");
        comics.Find("a.wl-cover").GetAttribute("href").ShouldBe($"wishlist/comics/{saga.Id}");
    }

    [Fact]
    public async Task A_wanted_films_page_shows_what_tmdb_knows_and_nothing_to_edit()
    {
        var heat = await WantHeatAsync();

        var page = RenderFilm(heat.Id);

        page.WaitForElement(".synopsis").TextContent.ShouldBe(TestMovies.Heat.Overview);
        Text(page.Find("h1.title-lg")).ShouldBe("Heat1995");
        Text(page.Find(".byline")).ShouldBe("Directed by Michael Mann");
        page.FindAll(".meta-row span:not(.sep)").Select(Text).ShouldBe(["R", "170 min", "Crime", "Drama", "Thriller"]);
        page.Find(".fmts .fmt").ClassList.ShouldContain("want-b");
        Tags(page).ShouldBe(["4K UHD"]);
        page.Find(".detail .poster img").GetAttribute("src")!.ShouldStartWith(CoverBase.ToString());
        page.FindAll(".cast .person .n").Select(n => n.TextContent).ShouldBe(["Al Pacino", "Robert De Niro"]);
        page.FindAll("input, textarea, select").ShouldBeEmpty("nothing here is edited");
        Text(page.Find(".topbar a.back")).ShouldBe("Wishlist");
        page.Find(".actions a.got-it").GetAttribute("href").ShouldBe($"movies/new?wish={heat.Id}");
        page.FindAll(".actions a")[1].GetAttribute("href").ShouldBe("https://www.youtube.com/watch?v=0xbBLJ1WGwQ");
        Text(page.Find(".panel .owned")).ShouldBe("On your wishlist Added Jan 2026");

        page.FindAll(".tabs button")[1].Click();

        page.FindAll(".dept").Select(d => d.GetAttribute("aria-label")).ShouldContain("Directing");
    }

    [Fact]
    public async Task The_wanted_format_can_be_changed_on_a_films_page()
    {
        await SeedAsync(new() { Title = "Heat", Year = 1995, TmdbId = 949, Formats = [new(MovieFormatKind.Dvd)] });
        var heat = await WantHeatAsync();
        var page = RenderFilm(heat.Id);

        FormatRows(page).ShouldBe(["4K UHD:Wanted", "Blu-ray:", "DVD:Owned", "Digital:"]);
        page.FindAll(".wanted-format .fmt-toggle")[2].HasAttribute("disabled").ShouldBeTrue("an owned format can't be wanted");
        Tags(page).ShouldBe(["4K UHD", "Upgrade · own DVD"]);

        page.FindAll(".wanted-format .fmt-toggle")[1].Click();

        page.WaitForAssertion(() => FormatRows(page).ShouldBe(["4K UHD:", "Blu-ray:Wanted", "DVD:Owned", "Digital:"]));
        Tags(page).ShouldBe(["Blu-ray", "Upgrade · own DVD"]);
        page.Find(".wanted-format .fmt-toggle[aria-checked='true'] .fmt").ClassList.ShouldContain("want-b");
        (await Api.GetWishlistMovieAsync(heat.Id, Ct))!.Format.ShouldBe(MovieFormatKind.BluRay);
    }

    [Fact]
    public async Task A_format_bought_meanwhile_cannot_be_chosen()
    {
        var heat = await WantHeatAsync();
        var page = RenderFilm(heat.Id);
        await SeedAsync(new() { Title = "Heat", Year = 1995, TmdbId = 949, Formats = [new(MovieFormatKind.Dvd)] });

        page.FindAll(".wanted-format .fmt-toggle")[2].Click();

        Text(page.WaitForElement(".wanted-format .problem")).ShouldBe("You already own Heat on DVD.");
        (await Api.GetWishlistMovieAsync(heat.Id, Ct))!.Format.ShouldBe(MovieFormatKind.Uhd4K);
    }

    [Fact]
    public async Task A_wanted_films_page_has_find_on_amazon_for_the_disc_wanted()
    {
        var heat = await WantHeatAsync();

        var page = RenderFilm(heat.Id);

        var amazon = page.WaitForElement(".actions a[href*='amazon']");
        Text(amazon).ShouldBe("Find on Amazon");
        amazon.GetAttribute("href").ShouldBe((await Api.GetWishlistMovieAsync(heat.Id, Ct)).ShouldNotBeNull().AmazonUrl.ShouldNotBeNull().AbsoluteUri);
        amazon.GetAttribute("href")!.ShouldStartWith("https://www.amazon.com/s?k=Heat%201995%20");
        amazon.GetAttribute("target").ShouldBe("_blank");
    }

    [Fact]
    public async Task A_film_wanted_on_digital_has_no_find_on_amazon()
    {
        var heat = await WantHeatAsync();
        await Api.ChangeWishlistFormatAsync(heat.Id, MovieFormatKind.Digital, Ct);

        var page = RenderFilm(heat.Id);

        page.WaitForElement(".actions a.got-it");
        page.FindAll("a[href*='amazon']").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wanted_comics_page_has_find_on_amazon()
    {
        var saga = await WantAsync(TestComics.Saga7);

        var page = RenderComic(saga.Id);

        var amazon = page.WaitForElement(".actions a[href*='amazon']");
        Text(amazon).ShouldBe("Find on Amazon");
        amazon.GetAttribute("href").ShouldBe("https://www.amazon.com/s?k=Saga%20%237%20comic");
    }

    [Fact]
    public async Task While_tmdb_is_down_a_films_page_shows_what_the_wishlist_keeps()
    {
        var heat = await WantHeatAsync();
        Online.Tmdb.FailWith = MetadataProblem.Unavailable;

        var page = RenderFilm(heat.Id);

        Text(page.WaitForElement(".no-details")).ShouldBe("Couldn't reach TMDB for the overview, cast and crew.");
        Text(page.Find("h1.title-lg")).ShouldBe("Heat1995");
        Text(page.Find(".byline")).ShouldBe("Directed by Michael Mann");
        page.FindAll(".synopsis, .tabs, .meta-row").ShouldBeEmpty();
        FormatRows(page).ShouldBe(["4K UHD:Wanted", "Blu-ray:", "DVD:", "Digital:"]);
    }

    [Fact]
    public async Task Removing_from_a_films_page_goes_back_to_the_wishlist()
    {
        var heat = await WantHeatAsync();
        var page = RenderFilm(heat.Id);

        page.Find(".actions button.remove").Click();

        Text(page.Find(".confirm")).ShouldBe("Remove Heat from your wishlist? Cancel Remove");
        page.Find(".confirm-actions button:not(.danger)").Click();
        page.FindAll(".confirm").ShouldBeEmpty();
        (await Api.ListWishlistAsync(cancellationToken: Ct)).MovieCount.ShouldBe(1, "cancelled");

        page.Find(".actions button.remove").Click();
        page.Find(".confirm button.danger").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist"));
        (await Api.ListWishlistAsync(cancellationToken: Ct)).MovieCount.ShouldBe(0);
    }

    [Fact]
    public void A_film_no_longer_wanted_says_so()
    {
        var page = RenderFilm(Guid.NewGuid());

        page.Find(".empty h1").TextContent.ShouldBe("Not on your wishlist");
        page.Find(".empty a").GetAttribute("href").ShouldBe("wishlist");
    }

    [Fact]
    public async Task A_wanted_comics_page_shows_what_metron_knows_and_nothing_to_edit()
    {
        var saga = await WantAsync(TestComics.Saga7);

        var page = RenderComic(saga.Id);

        page.WaitForElement(".synopsis").TextContent.ShouldBe("Marko and Alana arrive on Quietus.");
        Text(page.Find("h1.title-lg")).ShouldBe("Saga #7");
        page.FindAll(".byline").Select(Text).ShouldBe(["Saga (2012) · Image · November 2012", "Written by Brian K. Vaughan", "Art by Fiona Staples"]);
        Text(page.Find(".story")).ShouldBe("Chapter Seven");
        Text(page.Find(".poster .pd")).ShouldBe("#7");
        page.Find(".detail .poster img").GetAttribute("src")!.ShouldStartWith(CoverBase.ToString());
        page.FindAll("input, textarea, select, .fmt-toggle").ShouldBeEmpty("nothing here is edited");
        page.Find(".topbar a.back").GetAttribute("href").ShouldBe("wishlist?tab=comics");
        page.Find(".actions a.got-it").GetAttribute("href").ShouldBe($"comics/new?wish={saga.Id}");
        Text(page.Find(".panel .owned")).ShouldBe("Single issue Added Jan 2026");
        page.FindAll(".from-missing").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_issue_metron_doesnt_list_shows_what_the_wishlist_keeps()
    {
        Online.Metron.AddSeries(1234, new SeriesFacts("Saga", 2012, "Image", ["1", "2", "3"]));
        await SeedComicAsync(TestComics.SagaIssue("1"));
        await SeedComicAsync(TestComics.SagaIssue("3"));
        var two = (await Api.WantMissingIssuesAsync(new(1234, "2"), Ct)).Comic!;

        var page = RenderComic(two.Id);

        Text(page.WaitForElement(".no-details")).ShouldBe("Metron has no entry for it, so only what the wishlist keeps is shown.");
        Text(page.Find("h1.title-lg")).ShouldBe("Saga #2");
        Text(page.Find(".byline")).ShouldBe("Saga (2012) · Image");
        Text(page.Find(".from-missing")).ShouldBe("From missing issues");
    }

    [Fact]
    public async Task A_wanted_books_page_shows_its_isbn()
    {
        var book = await WantAsync(TestComics.SagaVolume1);

        var page = RenderComic(book.Id);

        Text(page.WaitForElement(".owned-code")).ShouldBe("ISBN 9781607066019");
        Text(page.Find("h1.title-lg")).ShouldBe("Saga, Volume One");
        page.FindAll(".byline").Select(Text).ShouldBe(["Trade paperback · Image Comics", "Written by Brian K. Vaughan"]);
        Text(page.Find(".panel .owned")).ShouldBe("Trade paperback Added Jan 2026");
    }

    [Fact]
    public async Task Removing_from_a_comics_page_goes_back_to_the_comics_tab()
    {
        var saga = await WantAsync(TestComics.Saga7);
        var page = RenderComic(saga.Id);

        page.Find(".actions button.remove").Click();

        Text(page.Find(".confirm")).ShouldBe("Remove Saga #7 from your wishlist? Cancel Remove");
        (await Api.ListWishlistAsync(cancellationToken: Ct)).ComicCount.ShouldBe(1, "not until it is confirmed");
        page.Find(".confirm button.danger").Click();

        page.WaitForAssertion(() => RelativeLocation.ShouldBe("wishlist?tab=comics"));
        (await Api.ListWishlistAsync(cancellationToken: Ct)).ComicCount.ShouldBe(0);
        RenderComic(saga.Id).Find(".empty h1").TextContent.ShouldBe("Not on your wishlist");
    }
}

/// <summary>A wishlist item's page on the phone: the same page in one column, with the bottom bar.</summary>
public sealed class PhoneWishlistDetailTests : PhoneTestContext
{
    [Fact]
    public async Task The_phone_opens_a_wanted_film_and_changes_its_format()
    {
        Online.Tmdb.Add(TestMovies.HeatOnTmdb);
        var heat = (await Api.WantMovieAsync(new() { Title = "Heat", Year = 1995, TmdbId = 949, Format = MovieFormatKind.Uhd4K }, Ct)).Movie!;
        Navigation.NavigateTo($"wishlist/movies/{heat.Id}");

        var page = Render<WishlistMovieDetail>(p => p.Add(x => x.Id, heat.Id));
        page.WaitForElement(".synopsis");
        page.FindAll(".wanted-format .fmt-toggle")[2].Click();

        page.WaitForAssertion(() => page.Find(".wanted-format .fmt-toggle[aria-checked='true'] .fmt").TextContent.ShouldBe("DVD"));
        (await Api.GetWishlistMovieAsync(heat.Id, Ct))!.Format.ShouldBe(MovieFormatKind.Dvd);
    }
}
