using MMDb.Infrastructure.Vault;

namespace MMDb.Infrastructure.Tests;

/// <summary>Names, YAML and markdown that survive any title a film can have.</summary>
public sealed class VaultTextTests
{
    [Theory]
    [InlineData("Heat", 1995, "Heat (1995)")]
    [InlineData("Mission: Impossible", 1996, "Mission - Impossible (1996)")]
    [InlineData("Face/Off", 1997, "Face-Off (1997)")]
    [InlineData("Who's Afraid of Virginia Woolf?", 1966, "Who's Afraid of Virginia Woolf (1966)")]
    [InlineData("\"Crocodile\" Dundee", 1986, "Crocodile Dundee (1986)")]
    [InlineData("#Alive", 2020, "Alive (2020)")]
    [InlineData("[REC]", 2007, "REC (2007)")]
    [InlineData("...And Justice for All", 1979, "And Justice for All (1979)")]
    [InlineData("  Lots   of\tspace  ", null, "Lots of space")]
    [InlineData("Untitled.", null, "Untitled")]
    [InlineData("???", null, "Untitled")]
    [InlineData("Con", null, "Con (film)")]
    [InlineData("Con", 2020, "Con (2020)")]
    public void Note_names_are_safe_file_names(string title, int? year, string expected) =>
        NoteNames.For(title, year).ShouldBe(expected);

    [Theory]
    [InlineData("Saga", "7", null, null, "Saga 7")]
    [InlineData("Saga", "7", "Cover B", null, "Saga 7 (Cover B)")]
    [InlineData("Saga", "7", null, 1, "Saga 7")]
    [InlineData("Saga", "7", null, 2, "Saga 7 (2nd printing)")]
    [InlineData("Saga", "7", "1:25 Variant", 3, "Saga 7 (1 -25 Variant) (3rd printing)")]
    [InlineData("X-Men", "1.MU", null, 11, "X-Men 1.MU (11th printing)")]
    [InlineData("Batman/Superman", "½", null, 22, "Batman-Superman ½ (22nd printing)")]
    public void Issue_note_names_have_no_hash(string series, string number, string? variant, int? printing, string expected) =>
        NoteNames.ForIssue(series, number, variant, printing).ShouldBe(expected);

    [Theory]
    [InlineData(new[] { "1", "2", "3", "5", "7", "8" }, "#1–3, #5, #7–8")]
    [InlineData(new[] { "8", "1" }, "#1, #8")]
    [InlineData(new[] { "4" }, "#4")]
    [InlineData(new string[0], "")]
    public void Issue_numbers_are_written_as_ranges(string[] numbers, string expected) =>
        IssueRanges.Format(numbers, ["1", "2", "3", "4", "5", "6", "7", "8"]).ShouldBe(expected);

    [Fact]
    public void Ranges_follow_the_series_order_not_the_digits() =>
        IssueRanges.Format(["1", "1.MU", "2", "Annual 1"], ["1", "1.MU", "2", "3", "Annual 1"]).ShouldBe("#1–2, #Annual 1");

    [Fact]
    public void Very_long_titles_are_cut_short() =>
        NoteNames.For(new string('a', 300), 2000).ShouldBe(new string('a', 120) + " (2000)");

    [Theory]
    [InlineData("Heat", "Heat")]
    [InlineData("PG-13", "PG-13")]
    [InlineData("Lt. Vincent Hanna", "Lt. Vincent Hanna")]
    [InlineData("Mission: Impossible", "\"Mission: Impossible\"")]
    [InlineData("1917", "\"1917\"")]
    [InlineData("2001-01-01", "\"2001-01-01\"")]
    [InlineData("No", "\"No\"")]
    [InlineData("Null", "\"Null\"")]
    [InlineData("#Alive", "\"#Alive\"")]
    [InlineData("[REC]", "\"[REC]\"")]
    [InlineData("- dash", "\"- dash\"")]
    [InlineData("Say \"when\"", "\"Say \\\"when\\\"\"")]
    [InlineData(@"back\slash", @"""back\\slash""")]
    [InlineData("Amélie", "Amélie")]
    public void Yaml_text_is_quoted_only_when_it_has_to_be(string value, string expected) =>
        Yaml.Text(value).ShouldBe(expected);

    [Theory]
    [InlineData("A *bold* [[link]] #tag", @"A \*bold\* \[\[link\]\] \#tag")]
    [InlineData("Line one\nline two", "Line one line two")]
    [InlineData("Plain words.", "Plain words.")]
    public void Markdown_text_is_escaped(string text, string expected) =>
        Markdown.Escape(text).ShouldBe(expected);

    [Theory]
    [InlineData("Michael Mann", "[[Michael Mann]]")]
    [InlineData("AC/DC", "AC/DC")]
    [InlineData("Mr. #1", @"Mr. \#1")]
    public void People_are_linked_when_their_name_can_be_a_link(string name, string expected) =>
        Markdown.PersonLink(name).ShouldBe(expected);

    [Fact]
    public void A_note_is_shelfs_only_with_its_marker_and_an_id()
    {
        var id = Guid.NewGuid();

        MovieNote.IdOf($"---\nmmdb_id: {id}\nmanaged_by: mmdb\n---\n# Heat").ShouldBe(id);
        MovieNote.IdOf($"---\r\nmanaged_by: \"mmdb\"\r\nmmdb_id: {id:N}\r\n---\r\n").ShouldBe(id);
        MovieNote.IdOf($"---\nmmdb_id: {id}\n---\n").ShouldBeNull();
        MovieNote.IdOf($"# Heat\nmmdb_id: {id}\nmanaged_by: mmdb\n").ShouldBeNull();
        MovieNote.IdOf("---\nmanaged_by: mmdb\n---\n").ShouldBeNull();
    }

    [Theory]
    [InlineData("# Heat\n## My notes\nMine.\n", "Mine.\n")]
    [InlineData("# Heat\n## My notes", "")]
    [InlineData("# Heat\n## My notes  \r\n\r\nMine.", "\r\nMine.")]
    [InlineData("# Heat\n## My notes and more\nNot these.", "")]
    [InlineData("# Heat\nNo heading.", "")]
    public void My_notes_are_everything_after_the_heading(string note, string expected) =>
        MovieNote.MyNotesOf(note).ShouldBe(expected);
}
