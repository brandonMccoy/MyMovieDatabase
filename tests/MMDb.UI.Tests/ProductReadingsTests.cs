using Microsoft.Extensions.Time.Testing;
using MMDb.UI.Scanning;

namespace MMDb.UI.Tests;

/// <summary>From what the camera reads frame by frame to the codes that join the scan queue.</summary>
public sealed class ProductReadingsTests : IDisposable
{
    private const string ComicUpc = "070989304307";
    private const string FilmUpc = "786936894424";
    private const string Isbn = "9781632154385";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly List<string> _reported = [];
    private readonly ProductReadings _readings;

    public ProductReadingsTests() => _readings = new(_clock, _reported.Add);

    public void Dispose() => _readings.Dispose();

    private void Frames(int count, string code, string? addOn = null)
    {
        for (var i = 0; i < count; i++)
        {
            _readings.Read(code, addOn);
            _clock.Advance(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public void A_upc_read_with_its_add_on_is_reported_at_once_as_one_code()
    {
        _readings.Read(ComicUpc, "00711");

        _reported.ShouldBe(["07098930430700711"]);
    }

    [Fact]
    public void A_two_digit_add_on_counts_once_two_frames_agree_on_it()
    {
        _readings.Read(ComicUpc, "04");
        _reported.ShouldBeEmpty("two digits are easily misread");

        _readings.Read(ComicUpc, null);
        _readings.Read(ComicUpc, "04");

        _reported.ShouldBe(["07098930430704"]);
    }

    [Fact]
    public void Two_digit_add_ons_that_disagree_are_not_reported()
    {
        _readings.Read(ComicUpc, "04");
        _readings.Read(ComicUpc, "38");
        _readings.Read(ComicUpc, "04");
        _reported.ShouldBeEmpty();

        _readings.Read(ComicUpc, "04");

        _reported.ShouldBe(["07098930430704"]);
    }

    [Fact]
    public void A_two_digit_add_on_seen_only_once_is_left_out()
    {
        _readings.Read(ComicUpc, "38");

        _clock.Advance(ProductReadings.WaitForAddOn);

        _reported.ShouldBe([ComicUpc]);
    }

    [Fact]
    public void A_upc_without_an_add_on_waits_a_moment_for_a_frame_that_shows_one()
    {
        Frames(3, ComicUpc);
        _reported.ShouldBeEmpty();

        _readings.Read(ComicUpc, "00711");

        _reported.ShouldBe([ComicUpc + "00711"]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _reported.Count.ShouldBe(1, "the wait ended when the add-on was read");
    }

    [Fact]
    public void A_upc_that_never_shows_an_add_on_is_reported_alone_after_the_wait()
    {
        Frames(4, FilmUpc);
        _reported.ShouldBeEmpty();

        _clock.Advance(ProductReadings.WaitForAddOn);

        _reported.ShouldBe([FilmUpc]);
    }

    [Fact]
    public void It_is_reported_after_the_wait_even_if_the_camera_has_moved_away()
    {
        _readings.Read(FilmUpc, null);

        _clock.Advance(ProductReadings.WaitForAddOn);

        _reported.ShouldBe([FilmUpc]);
    }

    [Fact]
    public void Frames_that_miss_the_add_on_after_it_was_read_are_not_another_product()
    {
        Frames(2, ComicUpc, "04");

        Frames(100, ComicUpc);
        _clock.Advance(ProductReadings.WaitForAddOn);

        _reported.ShouldBe([ComicUpc + "04"], "100 frames are longer than it is remembered for, but it never left the view");
    }

    [Fact]
    public void The_same_upc_alone_long_after_is_a_scan_of_its_own()
    {
        _readings.Read(ComicUpc, "00711");
        _clock.Advance(ProductReadings.RememberedFor);

        _readings.Read(ComicUpc, null);
        _clock.Advance(ProductReadings.WaitForAddOn);

        _reported.ShouldBe([ComicUpc + "00711", ComicUpc]);
    }

    [Fact]
    public void Moving_on_to_another_barcode_reports_the_one_that_was_waiting()
    {
        Frames(2, FilmUpc);

        _readings.Read(ComicUpc, "00711");

        _reported.ShouldBe([FilmUpc, ComicUpc + "00711"]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _reported.Count.ShouldBe(2);
    }

    [Fact]
    public void Two_issues_of_a_series_scanned_one_after_the_other_are_both_reported()
    {
        _readings.Read("759606200687", "00711");
        _readings.Read("759606200687", "00811");

        _reported.ShouldBe(["75960620068700711", "75960620068700811"]);
    }

    [Fact]
    public void A_books_isbn_is_reported_at_once_and_its_price_add_on_is_left_out()
    {
        _readings.MayHaveAddOn(Isbn).ShouldBeFalse();

        _readings.Read(Isbn, "51499");

        _reported.ShouldBe([Isbn]);
    }

    [Theory]
    [InlineData("07098930")]
    [InlineData("https://shelf.example/pair")]
    public void Codes_that_cant_have_an_add_on_are_reported_at_once(string code)
    {
        _readings.Read(code, null);

        _reported.ShouldBe([code]);
    }

    [Fact]
    public void While_only_films_are_scanned_nothing_waits_and_add_ons_are_left_out()
    {
        _readings.AddOns = false;
        _readings.MayHaveAddOn(FilmUpc).ShouldBeFalse();

        _readings.Read(FilmUpc, null);
        _readings.Read(ComicUpc, "04");

        _reported.ShouldBe([FilmUpc, ComicUpc]);
    }

    [Fact]
    public void Stopping_drops_what_was_still_waiting()
    {
        _readings.Read(FilmUpc, null);

        _readings.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(1));

        _reported.ShouldBeEmpty();
    }
}
