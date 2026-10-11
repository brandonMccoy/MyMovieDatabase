using MMDb.Testing;

namespace MMDb.Client.Tests;

/// <summary>Covers on the phone: fetched from the PC once, then kept.</summary>
public sealed class CoverShelfTests : IAsyncLifetime
{
    private readonly TempFolder _cache = new();
    private TestPc _pc = null!;
    private TestPhone _phone = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CoverShelf Covers => new(_phone.Link, _cache["covers"]);

    private int CoverRequests => _phone.Transport.Asked.Count(a => a.Contains("/covers/", StringComparison.Ordinal));

    public async ValueTask InitializeAsync()
    {
        _pc = await TestPc.StartAsync();
        _phone = new TestPhone(_pc);
        await _phone.PairAsync();
        await _pc.Online.Covers.SaveAsync("abc-heat.jpg", [1, 2, 3], Ct);
        await _pc.Online.Covers.SaveAsync("abc saga #7.png", [4, 5], Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _phone.Dispose();
        await _pc.DisposeAsync();
        _cache.Dispose();
    }

    [Fact]
    public async Task A_cover_comes_from_the_pc_with_its_type()
    {
        var jpeg = await Covers.GetAsync("abc-heat.jpg", cancellationToken: Ct);
        var png = await Covers.GetAsync("abc saga #7.png", cancellationToken: Ct);

        jpeg.ShouldNotBeNull().Bytes.ShouldBe([1, 2, 3]);
        jpeg.ContentType.ShouldBe("image/jpeg");
        png.ShouldNotBeNull().Bytes.ShouldBe([4, 5]);
        png.ContentType.ShouldBe("image/png");
    }

    [Fact]
    public async Task A_cover_is_fetched_once_and_then_served_from_the_phone_even_without_the_pc()
    {
        await Covers.GetAsync("abc-heat.jpg", cancellationToken: Ct);
        _phone.Transport.Down.Add(TestPc.Address);

        var kept = await Covers.GetAsync("abc-heat.jpg", cancellationToken: Ct);

        kept.ShouldNotBeNull().Bytes.ShouldBe([1, 2, 3]);
        kept.ContentType.ShouldBe("image/jpeg");
        CoverRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_cover_the_pc_doesnt_have_or_cant_send_is_simply_missing()
    {
        (await Covers.GetAsync("nope.jpg", cancellationToken: Ct)).ShouldBeNull();

        _phone.Transport.Down.Add(TestPc.Address);
        (await Covers.GetAsync("abc-heat.jpg", cancellationToken: Ct)).ShouldBeNull();

        Directory.Exists(_cache["covers"]).ShouldBeFalse("nothing was kept");
    }

    [Fact]
    public async Task A_page_showing_one_cover_large_gets_the_pcs_own_file_while_the_pc_is_in_reach()
    {
        var asked = _phone.Transport.Asked.Count;

        var full = await Covers.GetAsync("abc-heat.jpg", full: true, Ct);
        await Covers.GetAsync("abc-heat.jpg", full: true, Ct);

        full.ShouldNotBeNull().Bytes.ShouldBe([1, 2, 3]);
        _phone.Transport.Asked.Skip(asked).ShouldBe(["192.168.1.24/api/v1/covers/abc-heat.jpg"], "asked for once, without a size");
        File.Exists(Path.Combine(_cache["covers"], "full", "abc-heat.jpg")).ShouldBeTrue();
        File.Exists(Path.Combine(_cache["covers"], "abc-heat.jpg")).ShouldBeFalse("the smaller copy is another file");
    }

    [Fact]
    public async Task Away_from_the_pc_the_smaller_copy_does_and_nothing_waits_on_the_pc()
    {
        await Covers.GetAsync("abc-heat.jpg", cancellationToken: Ct);
        _phone.Transport.Down.Add(TestPc.Address);
        await _phone.Link.CheckAsync(Ct);
        var asked = _phone.Transport.Asked.Count;

        var cover = await Covers.GetAsync("abc-heat.jpg", full: true, Ct);

        cover.ShouldNotBeNull().Bytes.ShouldBe([1, 2, 3]);
        _phone.Transport.Asked.Count.ShouldBe(asked);
    }

    [Fact]
    public async Task Keeping_the_covers_shown_clears_out_large_ones_too_once_they_are_old()
    {
        await Covers.GetAsync("abc-heat.jpg", full: true, Ct);
        await Covers.GetAsync("abc saga #7.png", full: true, Ct);
        var gone = Path.Combine(_cache["covers"], "full", "abc-heat.jpg");
        File.SetLastWriteTimeUtc(gone, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(Path.Combine(_cache["covers"], "full", "abc saga #7.png"), DateTime.UtcNow.AddHours(-2));

        (await Covers.KeepAsync(["abc saga #7.png"], Ct)).ShouldBe(1);

        File.Exists(gone).ShouldBeFalse();
        File.Exists(Path.Combine(_cache["covers"], "full", "abc saga #7.png")).ShouldBeTrue();
        File.Exists(Path.Combine(_cache["covers"], "abc saga #7.png")).ShouldBeTrue("the smaller copy was fetched");
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../secrets.txt")]
    [InlineData("covers/abc-heat.jpg")]
    [InlineData(@"..\abc-heat.jpg")]
    public async Task Only_plain_file_names_are_looked_for(string name)
    {
        (await Covers.GetAsync(name, cancellationToken: Ct)).ShouldBeNull();

        CoverRequests.ShouldBe(0);
    }
}
