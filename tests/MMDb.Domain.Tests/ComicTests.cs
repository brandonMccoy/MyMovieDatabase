using MMDb.Domain.Comics;

namespace MMDb.Domain.Tests;

public sealed class ComicTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly ComicDetails Daredevil181 = new()
    {
        Series = "Daredevil",
        SeriesYear = 1964,
        Number = "181",
        Title = "Last Hand",
        Variant = "Cover A",
        Printing = 1,
        Publisher = "Marvel",
        CoverDate = new DateOnly(1982, 4, 1),
        Writers = ["Frank Miller"],
        Artists = ["Frank Miller", "Klaus Janson"],
        Barcode = "71486024229 5 18111",
        MetronId = 12345,
        MetronSeriesId = 678,
        IsKeyIssue = true,
        KeyNote = "Death of Elektra",
        CoverUrl = "https://static.metron.cloud/media/issue/2019/01/01/daredevil-181.jpg",
    };

    private static readonly ComicDetails SagaVolume1 = new()
    {
        Format = ComicFormat.TradePaperback,
        Title = "Saga, Volume One",
        Series = "Saga",
        Number = "1",
        Isbn = "978-1-60706-601-9",
    };

    [Fact]
    public void A_new_comic_keeps_every_field()
    {
        var comic = Comic.Create(Daredevil181, Now);

        comic.Details.ShouldBeEquivalentTo(Daredevil181.Normalized());
        comic.Barcode.ShouldNotBeNull().Supplement.ShouldBe("18111");
        comic.CreatedAt.ShouldBe(Now);
        comic.Version.ShouldBe(1);
        comic.DisplayName.ShouldBe("Daredevil #181");
    }

    [Fact]
    public void Input_is_tidied()
    {
        var comic = Comic.Create(new ComicDetails
        {
            Series = "  Saga ",
            Number = "#007",
            Variant = " ",
            Writers = ["Brian K. Vaughan", " brian k. vaughan", ""],
            Barcode = "7 09853 01744 8 00711",
            KeyNote = "Not a key issue, so dropped",
        }, Now);

        comic.Series.ShouldBe("Saga");
        comic.Number.ShouldBe("7");
        comic.Variant.ShouldBeNull();
        comic.Writers.ShouldBe(["Brian K. Vaughan"]);
        comic.Barcode!.Value.ShouldBe("70985301744800711");
        comic.KeyNote.ShouldBeNull();
    }

    [Theory]
    [InlineData(ComicFormat.TradePaperback, "Saga, Volume One", "Saga", "1", "Saga, Volume One")]
    [InlineData(ComicFormat.Hardcover, null, "Saga", "1", "Saga Vol. 1")]
    [InlineData(ComicFormat.Omnibus, null, "Daredevil by Frank Miller", null, "Daredevil by Frank Miller")]
    [InlineData(ComicFormat.SingleIssue, "Last Hand", "Daredevil", "181", "Daredevil #181")]
    public void Display_names_depend_on_the_format(ComicFormat format, string? title, string? series, string? number, string expected)
    {
        new ComicDetails { Format = format, Title = title, Series = series, Number = number }.DisplayName.ShouldBe(expected);
    }

    [Fact]
    public void A_single_issue_needs_a_series_and_a_number()
    {
        var errors = new ComicDetails().Validate();

        errors.Select(e => e.Field).ShouldBe([ComicFields.Series, ComicFields.Number]);
    }

    [Fact]
    public void A_collected_edition_needs_a_title_or_a_series()
    {
        new ComicDetails { Format = ComicFormat.TradePaperback }.Validate().ShouldHaveSingleError(ComicFields.Title);
        new ComicDetails { Format = ComicFormat.TradePaperback, Series = "Saga" }.Validate().ShouldBeEmpty();
        SagaVolume1.Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(nameof(ComicDetails.SeriesYear))]
    [InlineData(nameof(ComicDetails.Printing))]
    [InlineData(nameof(ComicDetails.Barcode))]
    [InlineData(nameof(ComicDetails.Isbn))]
    [InlineData(nameof(ComicDetails.CoverUrl))]
    [InlineData(nameof(ComicDetails.Number))]
    [InlineData(nameof(ComicDetails.KeyNote))]
    public void Bad_values_are_turned_down(string field)
    {
        var details = field switch
        {
            nameof(ComicDetails.SeriesYear) => Daredevil181 with { SeriesYear = 1800 },
            nameof(ComicDetails.Printing) => Daredevil181 with { Printing = 0 },
            nameof(ComicDetails.Barcode) => Daredevil181 with { Barcode = "714860242290 18111" },
            nameof(ComicDetails.Isbn) => Daredevil181 with { Isbn = "9781607066018" },
            nameof(ComicDetails.CoverUrl) => Daredevil181 with { CoverUrl = "http://example.com/cover.jpg" },
            nameof(ComicDetails.Number) => Daredevil181 with { Number = new string('1', ComicDetails.MaxNumberLength + 1) },
            _ => Daredevil181 with { KeyNote = new string('k', ComicDetails.MaxShortTextLength + 1) },
        };

        details.Validate().ShouldHaveSingleError(field);
        Should.Throw<DomainValidationException>(() => Comic.Create(details, Now));
    }

    [Fact]
    public void Editing_replaces_the_details_and_bumps_the_version()
    {
        var comic = Comic.Create(Daredevil181, Now);

        comic.Update(Daredevil181 with { Variant = "Newsstand", IsKeyIssue = false }, Now.AddDays(1));

        comic.Variant.ShouldBe("Newsstand");
        comic.IsKeyIssue.ShouldBeFalse();
        comic.KeyNote.ShouldBeNull();
        comic.Version.ShouldBe(2);
        comic.UpdatedAt.ShouldBe(Now.AddDays(1));
        comic.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void The_same_barcode_and_add_on_is_the_same_copy()
    {
        var comic = Comic.Create(Daredevil181, Now);

        comic.IsSameCopyAs(new ComicDetails { Barcode = "714860242295 18111" }).ShouldBeTrue();
        comic.IsSameCopyAs(Daredevil181 with { Barcode = "714860242295 18121" }).ShouldBeFalse(); // second cover
        comic.IsSameCopyAs(Daredevil181 with { Barcode = "714860242295 18112" }).ShouldBeFalse(); // second printing
    }

    [Fact]
    public void Without_barcodes_the_same_metron_issue_variant_and_printing_is_the_same_copy()
    {
        var comic = Comic.Create(Daredevil181 with { Barcode = null }, Now);

        comic.IsSameCopyAs(new ComicDetails { MetronId = 12345, Variant = "cover a" }).ShouldBeTrue();
        comic.IsSameCopyAs(new ComicDetails { MetronId = 12345, Variant = "Cover B" }).ShouldBeFalse();
        comic.IsSameCopyAs(new ComicDetails { MetronId = 12345, Variant = "Cover A", Printing = 2 }).ShouldBeFalse();
        comic.IsSameCopyAs(new ComicDetails { MetronId = 999, Variant = "Cover A" }).ShouldBeFalse();
    }

    [Fact]
    public void The_same_isbn_is_the_same_copy()
    {
        var trade = Comic.Create(SagaVolume1, Now);

        trade.IsSameCopyAs(new ComicDetails { Format = ComicFormat.TradePaperback, Isbn = "1607066017" }).ShouldBeTrue();
        trade.IsSameCopyAs(new ComicDetails { Format = ComicFormat.Hardcover, Isbn = "9781632154163" }).ShouldBeFalse();
    }

    [Fact]
    public void A_copy_typed_in_by_hand_is_never_the_same_as_another()
    {
        var comic = Comic.Create(new ComicDetails { Series = "Saga", Number = "7" }, Now);

        comic.IsSameCopyAs(new ComicDetails { Series = "Saga", Number = "7" }).ShouldBeFalse();
    }
}

public sealed class ComicSeriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issue_numbers_are_kept_tidy_and_in_order()
    {
        var series = ComicSeries.Create(678, Now, new SeriesFacts(" Daredevil ", 1964, "Marvel", ["#002", "1", "10", "2", "Annual 1", " "]));

        series.Name.ShouldBe("Daredevil");
        series.IssueNumbers.ShouldBe(["1", "2", "10", "Annual 1"]);
    }

    [Fact]
    public void A_series_is_fetched_again_after_a_week()
    {
        var series = ComicSeries.Create(678, Now, new SeriesFacts("Saga", 2012, "Image", ["1"]));

        series.IsStale(Now.AddDays(6)).ShouldBeFalse();
        series.IsStale(Now.AddDays(7)).ShouldBeTrue();
    }
}
