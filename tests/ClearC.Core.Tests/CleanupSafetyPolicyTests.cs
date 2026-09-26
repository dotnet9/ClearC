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

    /// <summary>用户目录名恰好叫 <c>repos</c> / <c>source</c> 时不能整项误拒（§4.1）。</summary>
    [Theory]
    [InlineData(@"C:\Users\repos\AppData\Local\Temp")]
    [InlineData(@"C:\Users\source\AppData\Local\npm-cache")]
    [InlineData(@"C:\Users\Documents\.nuget\packages")]
    public void Evaluate_IgnoresProtectedNamesInsideTheUserProfilePrefix(string location)
    {
        var item = CreateItem(location, CleanupRisk.Low);

        Assert.Equal(SafetyDecisionKind.Allowed, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void Evaluate_DeniesWhenAnyOfMultiplePathsIsProtected()
    {
        var item = CreateItem(@"C:\Temp", CleanupRisk.Low) with
        {
            Paths = [@"C:\Temp", @"C:\Users\test\Documents\sub"]
        };

        var decision = _policy.Evaluate(item, true);

        Assert.Equal(SafetyDecisionKind.Denied, decision.Kind);
        Assert.Contains("Documents", decision.Reason);
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
        var item = CreateCodexItem();

        Assert.Equal(SafetyDecisionKind.ConfirmationRequired, _policy.Evaluate(item, false).Kind);
        Assert.Equal(SafetyDecisionKind.Allowed, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void Evaluate_DeniesCodexCleanerKeyOnUnrecognizedItem()
    {
        var item = CreateCodexItem() with { Id = "other" };

        Assert.Equal(SafetyDecisionKind.Denied, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void Evaluate_DeniesCodexCleanerKeyPointingOutsideConversationDirectories()
    {
        var item = CreateCodexItem() with { Paths = [@"C:\Users\test\.codex\plugins"] };

        Assert.Equal(SafetyDecisionKind.Denied, _policy.Evaluate(item, true).Kind);
    }

    [Fact]
    public void EvaluateForPlan_ReportsReasonsWithoutThrowing()
    {
        var allowed = CreateItem(@"C:\Temp", CleanupRisk.Low);
        var medium = CreateItem(@"C:\Users\test\.nuget\packages", CleanupRisk.Medium) with { Id = "medium" };
        var protectedPath = CreateItem(@"C:\Users\test\Documents", CleanupRisk.Low) with { Id = "protected" };
        var notSelected = CreateItem(@"C:\Cache", CleanupRisk.Low) with { Id = "ignored" };
        var items = new[] { allowed, medium, protectedPath, notSelected };
        var selected = new HashSet<string>(StringComparer.Ordinal) { allowed.Id, "medium", "protected" };
        var acknowledged = new HashSet<string>(StringComparer.Ordinal) { allowed.Id, "protected" };

        var evaluations = _policy.EvaluateForPlan(items, selected, acknowledged);

        Assert.Equal(3, evaluations.Count);
        Assert.Equal(SafetyDecisionKind.Allowed, evaluations.Single(entry => entry.Item.Id == allowed.Id).Decision.Kind);
        Assert.Equal(SafetyDecisionKind.ConfirmationRequired, evaluations.Single(entry => entry.Item.Id == "medium").Decision.Kind);
        Assert.Contains("Documents", evaluations.Single(entry => entry.Item.Id == "protected").Decision.Reason);
    }

    [Fact]
    public void BuildPlan_RejectsUnacknowledgedRisk()
    {
        var item = CreateItem(@"C:\Users\test\.nuget\packages", CleanupRisk.Medium);

        Assert.Throws<InvalidOperationException>(() => _policy.BuildPlan([item], new HashSet<string> { item.Id }, new HashSet<string>()));
    }

    [Fact]
    public void BuildPlan_ListsEveryRejectedItemInTheMessage()
    {
        var medium = CreateItem(@"C:\Users\test\.nuget\packages", CleanupRisk.Medium) with { Id = "medium", DisplayName = "Medium" };
        var protectedPath = CreateItem(@"C:\Users\test\Documents", CleanupRisk.Low) with { Id = "protected", DisplayName = "Protected" };
        var selected = new HashSet<string>(StringComparer.Ordinal) { "medium", "protected" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => _policy.BuildPlan([medium, protectedPath], selected, new HashSet<string>()));

        Assert.Contains("Medium", exception.Message);
        Assert.Contains("Protected", exception.Message);
        Assert.Contains("Documents", exception.Message);
    }

    [Fact]
    public void BuildPlan_ReturnsOnlySelectedAndAllowedItems()
    {
        var allowed = CreateItem(@"C:\Temp", CleanupRisk.Low);
        var notSelected = CreateItem(@"C:\Cache", CleanupRisk.Low) with { Id = "ignored" };

        var plan = _policy.BuildPlan([allowed, notSelected], new HashSet<string> { allowed.Id }, new HashSet<string>());

        Assert.Equal(new[] { allowed }, plan);
    }

    private static CleanupItem CreateItem(string location, CleanupRisk risk) =>
        new("item", "Item", location, CleanupCategory.PackageCache, risk, 1, 1, "", "cleaner",
            CleanerKind: CleanerKind.DirectoryContents);

    private static CleanupItem CreateCodexItem() => new(
        "codex-data",
        "Codex 会话记录",
        @"C:\Users\test\.codex",
        CleanupCategory.ApplicationData,
        CleanupRisk.High,
        1,
        1,
        "description",
        "codex-conversations",
        CleanerKind: CleanerKind.CodexConversations,
        Paths: [@"C:\Users\test\.codex\sessions", @"C:\Users\test\.codex\archived_sessions"]);
}
