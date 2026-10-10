using MMDb.Domain.Movies;

namespace MMDb.Domain.Tests;

public sealed class ReleaseNameParserTests
{
    [Theory]
    [InlineData("Blade.Runner.2049.2017.2160p.UHD.mkv", "Blade Runner 2049", 2017)]
    [InlineData("arrival_2016.mp4", "arrival", 2016)]
    [InlineData("Parasite.2019.KOREAN.1080p.WEB-DL.mkv", "Parasite", 2019)]
    [InlineData("Spirited Away (2001).mkv", "Spirited Away", 2001)]
    [InlineData("The Thing 1982 Remastered.mkv", "The Thing", 1982)]
    [InlineData("Heat.1995.4K.HDR.DV.x265-GROUP.mkv", "Heat", 1995)]
    [InlineData("The.Lord.of.the.Rings.The.Return.of.the.King.2003.EXTENDED.1080p.BluRay.x264.mkv", "The Lord of the Rings The Return of the King", 2003)]
    [InlineData("Spider-Man.Into.the.Spider-Verse.2018.BDRip.avi", "Spider-Man Into the Spider-Verse", 2018)]
    [InlineData("Alien (1979) [Director's Cut].mkv", "Alien", 1979)]
    [InlineData("[YTS] Heat (1995) [1080p].mp4", "Heat", 1995)]
    [InlineData("(1995) Heat.mkv", "Heat", 1995)]
    [InlineData("Heat - 1995 - 1080p.mkv", "Heat", 1995)]
    [InlineData("Se7en.1995.REMUX.mkv", "Se7en", 1995)]
    public void The_title_and_year_are_read_from_a_release_name(string fileName, string title, int year)
    {
        var name = ReleaseNameParser.Parse(fileName);

        name.Title.ShouldBe(title);
        name.Year.ShouldBe(year);
        name.Text.ShouldBe($"{title} {year}");
    }

    [Theory]
    [InlineData("dune.mkv", "dune")]
    [InlineData("movie_final_v2.mp4", "movie final v2")]
    [InlineData("Heat.1080p.BluRay.x264.mkv", "Heat")]
    [InlineData("Parasite.KOREAN.1080p.WEB-DL.mkv", "Parasite")]
    [InlineData("The.Thing.Remastered.mkv", "The Thing")]
    [InlineData("Heat.4K.mkv", "Heat")]
    [InlineData("Breaking.Bad.S01E01.720p.mkv", "Breaking Bad")]
    public void A_name_without_a_year_gives_only_the_title(string fileName, string title)
    {
        var name = ReleaseNameParser.Parse(fileName);

        name.Title.ShouldBe(title);
        name.Year.ShouldBeNull();
        name.Text.ShouldBe(title);
    }

    [Theory]
    [InlineData("2001.A.Space.Odyssey.1968.1080p.mkv", "2001 A Space Odyssey", 1968)]
    [InlineData("2001.A.Space.Odyssey.mkv", "2001 A Space Odyssey", null)]
    [InlineData("1917.2019.mkv", "1917", 2019)]
    [InlineData("1917.mkv", "1917", null)]
    [InlineData("2012.2009.1080p.mkv", "2012", 2009)]
    [InlineData("Wonder.Woman.1984.2020.2160p.mkv", "Wonder Woman 1984", 2020)]
    [InlineData("Death Race 2000 (1975).mkv", "Death Race 2000", 1975)]
    public void A_number_in_the_title_is_not_taken_for_the_year(string fileName, string title, int? year)
    {
        var name = ReleaseNameParser.Parse(fileName);

        name.Title.ShouldBe(title);
        name.Year.ShouldBe(year);
    }

    [Fact]
    public void A_trailing_number_that_could_be_a_year_is_kept_in_the_text_to_search_again()
    {
        var name = ReleaseNameParser.Parse("Blade.Runner.2049.mkv");

        name.Title.ShouldBe("Blade Runner");
        name.Year.ShouldBe(2049);
        name.Text.ShouldBe("Blade Runner 2049");
    }

    [Theory]
    [InlineData("The.English.Patient.1996.1080p.mkv", "The English Patient", 1996)]
    [InlineData("The.English.Patient.1080p.mkv", "The English Patient", null)]
    [InlineData("Uncut.Gems.2019.WEBRip.mkv", "Uncut Gems", 2019)]
    [InlineData("A.Complete.Unknown.2024.mkv", "A Complete Unknown", 2024)]
    [InlineData("Charlottes.Web.2006.720p.mkv", "Charlottes Web", 2006)]
    [InlineData("Internal.Affairs.1990.DVDRip.mkv", "Internal Affairs", 1990)]
    public void Words_that_are_tags_elsewhere_stay_in_a_title(string fileName, string title, int? year)
    {
        var name = ReleaseNameParser.Parse(fileName);

        name.Title.ShouldBe(title);
        name.Year.ShouldBe(year);
    }

    [Theory]
    [InlineData(@"Heat (1995)\movie.mkv", "Heat", 1995)]
    [InlineData(@"Blade.Runner.2049.2017.2160p.UHD\br2049.mkv", "Blade Runner 2049", 2017)]
    [InlineData(@"Crime\Heat (1995)\Heat.1080p.mkv", "Heat", 1995)]
    public void A_file_without_a_year_takes_the_name_of_its_folder_when_that_has_one(string path, string title, int year)
    {
        var name = ReleaseNameParser.ParsePath(path);

        name.Title.ShouldBe(title);
        name.Year.ShouldBe(year);
    }

    [Theory]
    [InlineData(@"Heat (1995)\Heat.1986.mkv", "Heat", 1986)]
    [InlineData(@"New\dune.mkv", "dune", null)]
    [InlineData("dune.mkv", "dune", null)]
    public void A_folder_does_not_overrule_the_file(string path, string title, int? year)
    {
        var name = ReleaseNameParser.ParsePath(path);

        name.Title.ShouldBe(title);
        name.Year.ShouldBe(year);
    }

    [Theory]
    [InlineData("Heat.mkv", true)]
    [InlineData("HEAT.MP4", true)]
    [InlineData("heat.m4v", true)]
    [InlineData("heat.avi", true)]
    [InlineData("heat.mov", true)]
    [InlineData("heat.wmv", true)]
    [InlineData("heat.srt", false)]
    [InlineData("heat.nfo", false)]
    [InlineData("heat", false)]
    public void Only_video_files_are_films(string fileName, bool video)
    {
        ReleaseNameParser.IsVideoFile(fileName).ShouldBe(video);
    }

    [Theory]
    [InlineData("arrival", "Arrival", true)]
    [InlineData("Amelie", "Amélie", true)]
    [InlineData("Spider-Man Into the Spider-Verse", "Spider-Man: Into the Spider-Verse", true)]
    [InlineData("Mr and Mrs Smith", "Mr. & Mrs. Smith", true)]
    [InlineData("Heat", "Heat and Dust", false)]
    [InlineData("Heat", null, false)]
    public void A_title_is_recognised_whatever_its_case_accents_and_punctuation(string fromFile, string? title, bool same)
    {
        new ReleaseName(fromFile, fromFile, null).IsTitled(title).ShouldBe(same);
    }
}
