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
        var jpeg = await Covers.GetAsync("abc-heat.jpg", Ct);
        var png = await Covers.GetAsync("abc saga #7.png", Ct);

        jpeg.ShouldNotBeNull().Bytes.ShouldBe([1, 2, 3]);
        jpeg.ContentType.ShouldBe("image/jpeg");
        png.ShouldNotBeNull().Bytes.ShouldBe([4, 5]);
        png.ContentType.ShouldBe("image/png");
    }

    [Fact]
    public async Task A_cover_is_fetched_once_and_then_served_from_the_phone_even_without_the_pc()
    {
        await Covers.GetAsync("abc-heat.jpg", Ct);
        _phone.Transport.Down.Add(TestPc.Address);

        var kept = await Covers.GetAsync("abc-heat.jpg", Ct);

        kept.ShouldNotBeNull().Bytes.ShouldBe([1, 2, 3]);
        kept.ContentType.ShouldBe("image/jpeg");
        CoverRequests.ShouldBe(1);
    }

    [Fact]
    public async Task A_cover_the_pc_doesnt_have_or_cant_send_is_simply_missing()
    {
        (await Covers.GetAsync("nope.jpg", Ct)).ShouldBeNull();

        _phone.Transport.Down.Add(TestPc.Address);
        (await Covers.GetAsync("abc-heat.jpg", Ct)).ShouldBeNull();

        Directory.Exists(_cache["covers"]).ShouldBeFalse("nothing was kept");
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../secrets.txt")]
    [InlineData("covers/abc-heat.jpg")]
    [InlineData(@"..\abc-heat.jpg")]
    public async Task Only_plain_file_names_are_looked_for(string name)
    {
        (await Covers.GetAsync(name, Ct)).ShouldBeNull();

        CoverRequests.ShouldBe(0);
    }
}
