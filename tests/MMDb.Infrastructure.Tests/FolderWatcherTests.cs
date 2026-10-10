using MMDb.Infrastructure.Digital;
using MMDb.Testing;

namespace MMDb.Infrastructure.Tests;

/// <summary>The real file system watcher over a temp folder.</summary>
public sealed class FolderWatcherTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempFolder _folder = new();
    private readonly FolderWatcher _watcher = new() { Quiet = TimeSpan.FromMilliseconds(150) };
    private readonly SemaphoreSlim _told = new(0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _folder.Dispose();
        _told.Dispose();
    }

    private IDisposable Watch() => _watcher.Watch(_folder.Path, () => _told.Release());

    private Task<bool> ToldAsync() => _told.WaitAsync(Patience, Ct);

    [Fact]
    public async Task A_new_file_is_noticed()
    {
        using var watching = Watch();

        File.WriteAllBytes(_folder["Heat.1995.mkv"], []);

        (await ToldAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Files_in_subfolders_and_deleted_and_renamed_files_are_noticed()
    {
        Directory.CreateDirectory(_folder["Crime"]);
        using var watching = Watch();

        File.WriteAllBytes(_folder[@"Crime\Heat.1995.mkv"], []);
        (await ToldAsync()).ShouldBeTrue("a file arrived in a subfolder");

        File.Move(_folder[@"Crime\Heat.1995.mkv"], _folder[@"Crime\Heat (1995).mkv"]);
        (await ToldAsync()).ShouldBeTrue("a file was renamed");

        File.Delete(_folder[@"Crime\Heat (1995).mkv"]);
        (await ToldAsync()).ShouldBeTrue("a file was deleted");
    }

    [Fact]
    public async Task A_burst_of_changes_is_told_once()
    {
        using var watching = Watch();

        for (var i = 0; i < 20; i++)
        {
            File.WriteAllBytes(_folder[$"film{i}.mkv"], []);
        }

        (await ToldAsync()).ShouldBeTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(600), Ct);
        _told.CurrentCount.ShouldBe(0, "twenty files copied in one go are one change");
    }

    [Fact]
    public async Task Nothing_is_told_after_watching_stopped()
    {
        Watch().Dispose();

        File.WriteAllBytes(_folder["Heat.1995.mkv"], []);

        (await _told.WaitAsync(TimeSpan.FromMilliseconds(600), Ct)).ShouldBeFalse();
    }

    [Fact]
    public void A_folder_that_is_not_there_cannot_be_watched()
    {
        Should.Throw<IOException>(() => _watcher.Watch(_folder["gone"], () => { }));
    }
}
