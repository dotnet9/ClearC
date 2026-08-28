using ClearC.Core.Models;

namespace ClearC.Desktop.Infrastructure.Scanning;

internal sealed record CleanupTargetDefinition(
    string Id,
    string DisplayName,
    string Location,
    IReadOnlyList<string> Paths,
    CleanupCategory Category,
    CleanupRisk Risk,
    string Description,
    string? CleanerKey,
    TimeSpan? MinimumAge = null,
    bool RequiresElevation = false,
    bool IsProtected = false);
