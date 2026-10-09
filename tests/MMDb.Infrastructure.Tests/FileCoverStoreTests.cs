using MMDb.Infrastructure.Covers;

namespace MMDb.Infrastructure.Tests;

public sealed class FileCoverStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "shelf-covers-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FileCoverStore Store => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_saved_cover_is_a_file_in_the_folder_which_is_created_when_needed()
    {
        await Store.SaveAsync("abc-heat.jpg", [1, 2, 3], Ct);

        (await File.ReadAllBytesAsync(Path.Combine(_directory, "abc-heat.jpg"), Ct)).ShouldBe([1, 2, 3]);
        Directory.GetFiles(_directory).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Saving_again_replaces_the_cover()
    {
        await Store.SaveAsync("abc-heat.jpg", [1, 2, 3], Ct);

        await Store.SaveAsync("abc-heat.jpg", [9], Ct);

        (await File.ReadAllBytesAsync(Path.Combine(_directory, "abc-heat.jpg"), Ct)).ShouldBe([9]);
    }

    [Fact]
    public async Task A_deleted_cover_is_gone_and_deleting_twice_is_harmless()
    {
        await Store.SaveAsync("abc-heat.jpg", [1], Ct);

        Store.Delete("abc-heat.jpg");
        Store.Delete("abc-heat.jpg");

        File.Exists(Path.Combine(_directory, "abc-heat.jpg")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("../escape.jpg")]
    [InlineData(@"..\escape.jpg")]
    [InlineData(@"C:\Windows\escape.jpg")]
    [InlineData("sub/cover.jpg")]
    [InlineData("..")]
    [InlineData("")]
    public async Task Only_plain_file_names_are_accepted(string name)
    {
        await Should.ThrowAsync<ArgumentException>(() => Store.SaveAsync(name, [1], Ct));
        Should.Throw<ArgumentException>(() => Store.Delete(name));
    }
}
