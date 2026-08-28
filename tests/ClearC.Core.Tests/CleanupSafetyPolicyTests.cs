using ClearC.Core.Models;
using ClearC.Core.Safety;

namespace ClearC.Core.Tests;

public sealed class CleanupSafetyPolicyTests
{
    private readonly CleanupSafetyPolicy _policy = new();

    [Fact]
    public void Evaluate_AllowsLowRiskCache()
    {
        var item = CreateItem(@"C:\Users\test\AppData\Local\Temp", CleanupRisk.Low);

        Assert.Equal(SafetyDecisionKind.Allowed, _policy.Evaluate(item, false).Kind);
    }

    [Theory]
    [InlineData(@"C:\Users\test\Documents\report.docx")]
    [InlineData(@"C:\Users\test\source\project")]
    [InlineData(@"C:\Users\test\.codex\sessions")]
    [InlineData(@"C:\work\repo\.git\objects")]
    public void Evaluate_DeniesProtectedUserAndSourcePaths(string location)
    {
        var item = CreateItem(location, CleanupRisk.Low);

        Assert.Equal(SafetyDecisionKind.Denied, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void Evaluate_RequiresExplicitAcknowledgementForMediumRisk()
    {
        var item = CreateItem(@"C:\Users\test\.nuget\packages", CleanupRisk.Medium);

        Assert.Equal(SafetyDecisionKind.ConfirmationRequired, _policy.Evaluate(item, false).Kind);
        Assert.Equal(SafetyDecisionKind.Allowed, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void Evaluate_AllowsOnlyAcknowledgedCodexConversationCleaner()
    {
        var item = new CleanupItem(
            "codex-data",
            "Codex 会话记录",
            @"C:\Users\test\.codex",
            CleanupCategory.ApplicationData,
            CleanupRisk.High,
            1,
            1,
            "description",
            "codex-conversations");

        Assert.Equal(SafetyDecisionKind.ConfirmationRequired, _policy.Evaluate(item, false).Kind);
        Assert.Equal(SafetyDecisionKind.Allowed, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void Evaluate_DeniesCodexCleanerKeyOnUnrecognizedItem()
    {
        var item = new CleanupItem(
            "other",
            "Other",
            @"C:\Users\test\.codex",
            CleanupCategory.ApplicationData,
            CleanupRisk.High,
            1,
            1,
            "description",
            "codex-conversations");

        Assert.Equal(SafetyDecisionKind.Denied, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void BuildPlan_RejectsUnacknowledgedRisk()
    {
        var item = CreateItem(@"C:\Users\test\.nuget\packages", CleanupRisk.Medium);

        Assert.Throws<InvalidOperationException>(() => _policy.BuildPlan([item], new HashSet<string> { item.Id }, new HashSet<string>()));
    }

    private static CleanupItem CreateItem(string location, CleanupRisk risk) =>
        new("item", "Item", location, CleanupCategory.PackageCache, risk, 1, 1, "", "cleaner");
}
