using MMDb.Infrastructure.Persistence;

namespace MMDb.Infrastructure.Tests;

public sealed class SettingStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_setting_is_kept_changed_and_removed()
    {
        using var database = await TestDatabase.MigratedAsync();
        var store = new SettingStore(database.Options);

        (await store.GetAsync("vault.folder", Ct)).ShouldBeNull();

        await store.SetAsync("vault.folder", @"C:\Vault", Ct);
        (await new SettingStore(database.Options).GetAsync("vault.folder", Ct)).ShouldBe(@"C:\Vault");

        await store.SetAsync("vault.folder", @"D:\Other", Ct);
        (await store.GetAsync("vault.folder", Ct)).ShouldBe(@"D:\Other");

        await store.SetAsync("vault.folder", null, Ct);
        (await store.GetAsync("vault.folder", Ct)).ShouldBeNull();
    }
}
