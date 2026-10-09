using System.Text.RegularExpressions;

namespace MMDb.UI.Tests;

/// <summary>The app must look right offline, so every font the tokens ask for ships inside MMDb.UI.</summary>
public sealed partial class BundledFontTests
{
    private static readonly string CssDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "css");
    private static readonly string FontsCss = File.ReadAllText(Path.Combine(CssDirectory, "fonts.css"));
    private static readonly byte[] TrueTypeSignature = [0x00, 0x01, 0x00, 0x00];

    [Fact]
    public void Every_font_face_points_to_a_bundled_TrueType_file()
    {
        var urls = FontUrl().Matches(FontsCss).Select(m => m.Groups["url"].Value).ToList();

        urls.ShouldNotBeEmpty();
        foreach (var url in urls)
        {
            var path = Path.GetFullPath(Path.Combine(CssDirectory, url));
            File.Exists(path).ShouldBeTrue($"{url} is not bundled");
            File.ReadAllBytes(path).Take(4).ShouldBe(TrueTypeSignature, $"{url} is not a TrueType font");
        }
    }

    [Theory]
    [InlineData("--font")]
    [InlineData("--serif")]
    [InlineData("--mono")]
    public void The_preferred_family_of_each_font_token_is_bundled(string token)
    {
        var preferred = DesignTokens.Load()[token].Split(',')[0].Trim().Trim('"');
        var bundled = FontFamily().Matches(FontsCss).Select(m => m.Groups["family"].Value);

        bundled.ShouldContain(preferred);
    }

    [Fact]
    public void Each_bundled_family_ships_with_its_license()
    {
        var families = FontFamily().Matches(FontsCss).Select(m => m.Groups["family"].Value).Distinct();

        foreach (var family in families)
        {
            var license = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts", $"OFL-{family.Replace(" ", "", StringComparison.Ordinal)}.txt");
            File.Exists(license).ShouldBeTrue($"missing license for {family}");
        }
    }

    [GeneratedRegex("""url\("(?<url>[^"]+)"\)""")]
    private static partial Regex FontUrl();

    [GeneratedRegex("""font-family:\s*"(?<family>[^"]+)";""")]
    private static partial Regex FontFamily();
}
