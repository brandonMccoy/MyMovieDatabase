using System.Net;
using MMDb.Application.Metadata;
using MMDb.Domain;
using MMDb.Infrastructure.Metadata;

namespace MMDb.Infrastructure.Tests;

public sealed class UpcItemDbClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UpcItemDbClient Client(FakeHttp http) => new(http.Client("https://api.upcitemdb.com/"));

    [Fact]
    public async Task A_known_barcode_gives_the_product_name()
    {
        var http = FakeHttp.Replaying("upcitemdb/fight-club-bluray.json");

        var names = await Client(http).FindProductNamesAsync(Upc.Parse("024543617907"), Ct);

        names.ShouldBe(["Mill Creek - Fight Club [BLU-RAY]"]);
        http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://api.upcitemdb.com/prod/trial/lookup?upc=024543617907"));
    }

    [Theory]
    [InlineData("upcitemdb/not-found.json", HttpStatusCode.OK)]
    [InlineData("upcitemdb/invalid-upc.json", HttpStatusCode.BadRequest)]
    public async Task An_unknown_barcode_has_no_names(string fixture, HttpStatusCode status)
    {
        (await Client(FakeHttp.Replaying(fixture, status)).FindProductNamesAsync(Upc.Parse("895127634104"), Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Too_many_lookups_is_reported_as_busy()
    {
        var ex = await Should.ThrowAsync<MetadataException>(() =>
            Client(FakeHttp.Replaying("upcitemdb/too-fast.json", HttpStatusCode.TooManyRequests)).FindProductNamesAsync(Upc.Parse("024543617907"), Ct));

        ex.Problem.ShouldBe(MetadataProblem.Busy);
        ex.Service.ShouldBe("UPCitemdb");
    }

    [Fact]
    public async Task A_failing_service_is_reported_as_unavailable()
    {
        var http = new FakeHttp(_ => FakeHttp.Respond(HttpStatusCode.InternalServerError, "{}"));

        var ex = await Should.ThrowAsync<MetadataException>(() => Client(http).FindProductNamesAsync(Upc.Parse("024543617907"), Ct));

        ex.Problem.ShouldBe(MetadataProblem.Unavailable);
    }
}
