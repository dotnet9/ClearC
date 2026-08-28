using ClearC.Desktop.Infrastructure.Cleanup;

namespace ClearC.Desktop.Tests;

public sealed class CodexConversationCleanerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ClearC.Codex.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task CleanAsync_DeletesOnlySessionDirectoryContents()
    {
        var codexHome = Path.Combine(_root, ".codex");
        var sessions = Path.Combine(codexHome, "sessions");
        var archivedSessions = Path.Combine(codexHome, "archived_sessions");
        Directory.CreateDirectory(Path.Combine(sessions, "2026", "08"));
        Directory.CreateDirectory(archivedSessions);
        var active = Path.Combine(sessions, "2026", "08", "rollout.jsonl");
        var archived = Path.Combine(archivedSessions, "archived.jsonl");
        var config = Path.Combine(codexHome, "config.toml");
        await File.WriteAllBytesAsync(active, new byte[32], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(archived, new byte[64], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(config, "model = 'test'", TestContext.Current.CancellationToken);
        var cleaner = new CodexConversationCleaner(new GuardedDirectoryCleaner(), new FakeProcessDetector(false));

        var result = await cleaner.CleanAsync(
            [sessions, archivedSessions],
            TestContext.Current.CancellationToken);

        Assert.Null(result.FatalError);
        Assert.Equal(96, result.FreedBytes);
        Assert.Equal(2, result.DeletedFiles);
        Assert.False(File.Exists(active));
        Assert.False(File.Exists(archived));
        Assert.True(File.Exists(config));
        Assert.True(Directory.Exists(sessions));
        Assert.True(Directory.Exists(archivedSessions));
    }

    [Fact]
    public async Task CleanAsync_SkipsEverythingWhileCodexIsRunning()
    {
        var codexHome = Path.Combine(_root, ".codex");
        var sessions = Path.Combine(codexHome, "sessions");
        var archivedSessions = Path.Combine(codexHome, "archived_sessions");
        Directory.CreateDirectory(sessions);
        Directory.CreateDirectory(archivedSessions);
        var session = Path.Combine(sessions, "rollout.jsonl");
        await File.WriteAllBytesAsync(session, new byte[32], TestContext.Current.CancellationToken);
        var cleaner = new CodexConversationCleaner(new GuardedDirectoryCleaner(), new FakeProcessDetector(true));

        var result = await cleaner.CleanAsync(
            [sessions, archivedSessions],
            TestContext.Current.CancellationToken);

        Assert.True(result.CodexIsRunning);
        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(session));
    }

    [Fact]
    public async Task CleanAsync_RejectsTargetsOutsideSessionDirectories()
    {
        var codexHome = Path.Combine(_root, ".codex");
        var sessions = Path.Combine(codexHome, "sessions");
        var plugins = Path.Combine(codexHome, "plugins");
        Directory.CreateDirectory(sessions);
        Directory.CreateDirectory(plugins);
        var plugin = Path.Combine(plugins, "plugin.json");
        await File.WriteAllTextAsync(plugin, "{}", TestContext.Current.CancellationToken);
        var cleaner = new CodexConversationCleaner(new GuardedDirectoryCleaner(), new FakeProcessDetector(false));

        var result = await cleaner.CleanAsync(
            [sessions, plugins],
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.FatalError);
        Assert.True(File.Exists(plugin));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class FakeProcessDetector(bool isRunning) : ICodexProcessDetector
    {
        public bool IsRunning() => isRunning;
    }
}
