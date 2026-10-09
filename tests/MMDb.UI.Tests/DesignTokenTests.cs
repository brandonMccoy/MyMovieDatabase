namespace MMDb.UI.Tests;

/// <summary>Guards the look decided in docs/design/DECISIONS.md and mockup-v1.</summary>
public sealed class DesignTokenTests
{
    private static readonly DesignTokens Tokens = DesignTokens.Load();

    private static readonly string[] TextTokens = ["--text", "--text-2", "--muted"];
    private static readonly string[] BadgeTokens = ["--uhd", "--bluray", "--dvd", "--digital"];
    private static readonly string[] ChartTokens = ["--c-uhd", "--c-br", "--c-dvd", "--c-digital"];
    private static readonly string[] ReadingSurfaces = ["--bg", "--surface", "--surface-2"];
    private static readonly string[] AllSurfaces = ["--bg", "--surface", "--surface-2", "--surface-3"];

    [Theory]
    [InlineData("--accent", "#e8b04b")]
    [InlineData("--uhd", "#e8b04b")]
    [InlineData("--bluray", "#4c8dff")]
    [InlineData("--dvd", "#d77cc0")]
    [InlineData("--digital", "#5cc489")]
    [InlineData("--c-uhd", "#bf8a2c")]
    [InlineData("--c-br", "#3a78e0")]
    [InlineData("--c-dvd", "#c05aa8")]
    [InlineData("--c-digital", "#3fa36b")]
    public void Decided_colors_keep_their_values(string token, string expected)
    {
        Tokens[token].ShouldBe(expected, StringCompareShould.IgnoreCase);
    }

    [Theory]
    [InlineData("--font", "\"Instrument Sans\"")]
    [InlineData("--serif", "\"Instrument Serif\"")]
    public void Decided_fonts_come_first_in_their_stacks(string token, string family)
    {
        Tokens[token].ShouldStartWith(family);
    }

    public static TheoryData<string, string> TextOnReadingSurfaces => Pairs(TextTokens, ReadingSurfaces);

    [Theory]
    [MemberData(nameof(TextOnReadingSurfaces))]
    public void Text_meets_WCAG_AA_on_page_and_card_surfaces(string text, string surface)
    {
        Tokens.Contrast(text, surface).ShouldBeGreaterThanOrEqualTo(4.5);
    }

    public static TheoryData<string, string> BadgesOnReadingSurfaces => Pairs(BadgeTokens, ReadingSurfaces);

    [Theory]
    [MemberData(nameof(BadgesOnReadingSurfaces))]
    public void Format_badge_text_meets_WCAG_AA_on_page_and_card_surfaces(string badge, string surface)
    {
        Tokens.Contrast(badge, surface).ShouldBeGreaterThanOrEqualTo(4.5);
    }

    public static TheoryData<string, string> ChartsOnAllSurfaces => Pairs(ChartTokens, AllSurfaces);

    [Theory]
    [MemberData(nameof(ChartsOnAllSurfaces))]
    public void Chart_colors_stand_out_from_every_surface(string chart, string surface)
    {
        // WCAG 1.4.11 non-text contrast.
        Tokens.Contrast(chart, surface).ShouldBeGreaterThanOrEqualTo(3.0);
    }

    [Fact]
    public void Text_on_the_accent_meets_WCAG_AA()
    {
        Tokens.Contrast("--accent-ink", "--accent").ShouldBeGreaterThanOrEqualTo(4.5);
    }

    private static TheoryData<string, string> Pairs(string[] foregrounds, string[] backgrounds)
    {
        var data = new TheoryData<string, string>();
        foreach (var foreground in foregrounds)
        {
            foreach (var background in backgrounds)
            {
                data.Add(foreground, background);
            }
        }

        return data;
    }
}
