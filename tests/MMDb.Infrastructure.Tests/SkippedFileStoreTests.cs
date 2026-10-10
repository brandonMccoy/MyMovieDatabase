using Microsoft.Extensions.Time.Testing;
using MMDb.Infrastructure.Persistence;

namespace MMDb.Infrastructure.Tests;

public sealed class SkippedFileStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Skipped_files_are_kept_until_removed()
    {
        using var database = await TestDatabase.MigratedAsync();
        var store = new SkippedFileStore(database.Options, new FakeTimeProvider());

        (await store.ListAsync(Ct)).ShouldBeEmpty();

        await store.AddAsync(@"D:\Movies\Extras\making-of.mkv", Ct);
        await store.AddAsync(@"D:\Movies\sample.mkv", Ct);

        (await new SkippedFileStore(database.Options, new FakeTimeProvider()).ListAsync(Ct))
            .ShouldBe([@"D:\Movies\Extras\making-of.mkv", @"D:\Movies\sample.mkv"], ignoreOrder: true);

        await store.RemoveAsync(@"D:\Movies\sample.mkv", Ct);
        await store.RemoveAsync(@"D:\Movies\never-skipped.mkv", Ct);

        (await store.ListAsync(Ct)).ShouldBe([@"D:\Movies\Extras\making-of.mkv"]);
    }

    [Fact]
    public async Task A_path_is_the_same_file_whatever_its_case()
    {
        using var database = await TestDatabase.MigratedAsync();
        var store = new SkippedFileStore(database.Options, new FakeTimeProvider());

        await store.AddAsync(@"D:\Movies\Sample.mkv", Ct);
        await store.AddAsync(@"d:\movies\sample.MKV", Ct);

        (await store.ListAsync(Ct)).ShouldBe([@"D:\Movies\Sample.mkv"]);

        await store.RemoveAsync(@"D:\MOVIES\SAMPLE.MKV", Ct);

        (await store.ListAsync(Ct)).ShouldBeEmpty();
    }
}
