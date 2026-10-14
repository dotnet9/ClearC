using ClearC.Desktop.I18n;
using System.Diagnostics;

namespace ClearC.Desktop.Infrastructure.Cleanup;

internal sealed record CodexConversationCleanupResult(
    long FreedBytes,
    long DeletedFiles,
    long SkippedFiles,
    bool CodexIsRunning = false,
    string? FatalError = null);

internal interface ICodexConversationCleaner
{
    Task<CodexConversationCleanupResult> CleanAsync(
        IReadOnlyList<string> conversationRoots,
        CancellationToken cancellationToken);
}

internal interface ICodexProcessDetector
{
    bool IsRunning();
}

internal sealed class CodexConversationCleaner : ICodexConversationCleaner
{
    private static readonly HashSet<string> AllowedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sessions",
        "archived_sessions"
    };

    private readonly IGuardedDirectoryCleaner _directoryCleaner;
    private readonly ICodexProcessDetector _processDetector;

    public CodexConversationCleaner()
        : this(new GuardedDirectoryCleaner(), new CodexProcessDetector())
    {
    }

    internal CodexConversationCleaner(
        IGuardedDirectoryCleaner directoryCleaner,
        ICodexProcessDetector processDetector)
    {
        _directoryCleaner = directoryCleaner;
        _processDetector = processDetector;
    }

    public async Task<CodexConversationCleanupResult> CleanAsync(
        IReadOnlyList<string> conversationRoots,
        CancellationToken cancellationToken)
    {
        try
        {
            var validatedRoots = ValidateRoots(conversationRoots);
            if (_processDetector.IsRunning())
            {
                return new(0, 0, 0, CodexIsRunning: true);
            }

            var result = await _directoryCleaner.CleanContentsAsync(
                new DirectoryCleanupRequest(validatedRoots, ModifiedBefore: null),
                cancellationToken);
            return new(result.FreedBytes, result.DeletedFiles, result.SkippedFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            UnauthorizedAccessException or
            IOException or
            NotSupportedException)
        {
            return new(0, 0, 0, FatalError: exception.Message);
        }
    }

    private static string[] ValidateRoots(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(AppText.Get(Language.RuntimeCodexAbsoluteDirectory));
        }

        var normalizedRoots = roots
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedRoots.Length != AllowedDirectoryNames.Count)
        {
            throw new InvalidOperationException(AppText.Get(Language.RuntimeCodexExactRoots));
        }

        string? codexHome = null;
        foreach (var root in normalizedRoots)
        {
            var directoryName = Path.GetFileName(root);
            var parent = Directory.GetParent(root)?.FullName;
            if (!AllowedDirectoryNames.Contains(directoryName) ||
                parent is null ||
                !string.Equals(Path.GetFileName(parent), ".codex", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(AppText.Get(Language.RuntimeCodexOutsideRoots));
            }

            codexHome ??= parent;
            if (!string.Equals(codexHome, parent, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(AppText.Get(Language.RuntimeCodexCommonRoot));
            }

            if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(AppText.Get(Language.RuntimeCodexReparseRoot));
            }
        }

        return normalizedRoots;
    }
}

internal sealed class CodexProcessDetector : ICodexProcessDetector
{
    public bool IsRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.StartsWith("codex", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is
                    InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
                {
                }
            }
        }

        return false;
    }
}
