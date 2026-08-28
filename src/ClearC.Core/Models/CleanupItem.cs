namespace ClearC.Core.Models;

public sealed record CleanupItem(
    string Id,
    string DisplayName,
    string Location,
    CleanupCategory Category,
    CleanupRisk Risk,
    long SizeBytes,
    long FileCount,
    string Description,
    string? CleanerKey,
    bool RequiresElevation = false,
    bool IsProtected = false)
{
    public bool CanClean => CleanerKey is not null && !IsProtected;

    public bool IsRecommended => CanClean && SizeBytes > 0 && Risk == CleanupRisk.Low;
}
