using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class FormatDetailsTests
{
    [Theory]
    [InlineData(MediaFormat.Uhd4K)]
    [InlineData(MediaFormat.BluRay)]
    [InlineData(MediaFormat.Dvd)]
    [InlineData(MediaFormat.Digital)]
    public void A_format_without_barcode_or_file_is_valid(MediaFormat format)
    {
        new FormatDetails(format).Validate().ShouldBeEmpty();
    }

    [Fact]
    public void A_disc_with_a_valid_barcode_is_valid()
    {
        new FormatDetails(MediaFormat.Uhd4K, Upc: "786936894424").Validate().ShouldBeEmpty();
    }

    [Fact]
    public void An_invalid_barcode_is_reported_against_its_format()
    {
        new FormatDetails(MediaFormat.BluRay, Upc: "786936894421").Validate().ShouldHaveSingleError("BluRay.Upc");
    }

    [Fact]
    public void A_digital_copy_may_have_a_file_path()
    {
        new FormatDetails(MediaFormat.Digital, FilePath: @"D:\Movies\Heat (1995).mkv").Validate().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MediaFormat.Uhd4K)]
    [InlineData(MediaFormat.BluRay)]
    [InlineData(MediaFormat.Dvd)]
    public void Discs_cannot_have_a_file_path(MediaFormat format)
    {
        new FormatDetails(format, FilePath: @"D:\Movies\Heat.mkv").Validate().ShouldHaveSingleError($"{format}.FilePath");
    }

    [Fact]
    public void An_undefined_format_is_rejected()
    {
        new FormatDetails((MediaFormat)42).Validate().ShouldHaveSingleError(MovieFields.Formats);
    }

    [Fact]
    public void A_film_must_be_owned_on_at_least_one_format()
    {
        FormatDetails.ValidateSet([]).ShouldHaveSingleError(MovieFields.Formats);
    }

    [Fact]
    public void A_format_cannot_be_listed_twice()
    {
        var errors = FormatDetails.ValidateSet([new(MediaFormat.Dvd), new(MediaFormat.Dvd, Upc: "786936894424")]);

        errors.ShouldHaveSingleError(MovieFields.Formats).Message.ShouldContain("DVD");
    }

    [Fact]
    public void Each_format_in_a_set_is_validated()
    {
        var errors = FormatDetails.ValidateSet([new(MediaFormat.Dvd, Upc: "123"), new(MediaFormat.BluRay, FilePath: "x.mkv")]);

        errors.Select(e => e.Field).ShouldBe(["Dvd.Upc", "BluRay.FilePath"], ignoreOrder: true);
    }
}
